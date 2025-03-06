// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

#if !TASKHOST
using System.Buffers.Binary;
using System.Threading.Tasks;
using Microsoft.Build.Eventing;
#endif

namespace Microsoft.Build.Internal
{
    internal abstract class NodePipeBase : IDisposable
    {
        private const int HeaderLength = 5;

        /// <summary>
        /// </summary>
        /// <remarks>
        /// Begin with the maximum amount before allocating to the large object heap.
        /// </remarks>
        private const int InitialBufferSize = 81_920;

        private readonly byte[] _headerData = new byte[HeaderLength];

        /// <summary>
        /// A buffer typically big enough to handle a packet body.
        /// We use this as a convenient way to manage and cache a byte[] that's resized
        /// automatically to fit our payload.
        /// </summary>
        private readonly MemoryStream _readBuffer = new(InitialBufferSize);

        /// <summary>
        /// A way to cache a byte array when writing out packets.
        /// </summary>
        private readonly MemoryStream _writeBuffer = new(InitialBufferSize);

        private readonly ITranslator _readTranslator;

        private readonly ITranslator _writeTranslator;

        private INodePacketFactory? _packetFactory;

        protected NodePipeBase()
        {
            _readTranslator = BinaryTranslator.GetReadTranslator(_readBuffer, InterningBinaryReader.CreateSharedBuffer());
            _writeTranslator = BinaryTranslator.GetWriteTranslator(_writeBuffer);
        }

        protected abstract Stream NodeStream { get; }

        public void Dispose()
        {
            _readBuffer.Dispose();
            _writeBuffer.Dispose();
            _readTranslator.Dispose();
            _writeTranslator.Dispose();
            NodeStream.Dispose();
        }

        internal void RegisterPacketFactory(INodePacketFactory packetFactory) => _packetFactory = packetFactory;

        internal void WritePacket(INodePacket packet)
        {
            // Clear the buffer but keep the underlying capacity to avoid reallocations.
            _writeBuffer.SetLength(HeaderLength);
            _writeBuffer.Position = HeaderLength;

            // Serialize and write the packet to the buffer.
            packet.Translate(_writeTranslator);

            // Write the header to the buffer.
            _writeBuffer.Position = 0;
            _writeBuffer.WriteByte((byte)packet.Type);
            int messageLength = (int)_writeBuffer.Length;
            _writeTranslator.Writer.Write(messageLength - HeaderLength);

            // Copy the buffer contents into the output stream.
            NodeStream.Write(_writeBuffer.GetBuffer(), 0, messageLength);
        }

        internal INodePacket ReadPacket()
        {
            // Read the header.
            int headerBytesRead = Read(_headerData, HeaderLength);

            // When an active connection is broken, any pending read will return 0 bytes before the pipe transitions to
            // the broken state. As this is expected behavior, don't throw an exception if no packet is pending, A node
            // may disconnect without waiting on the other end to gracefully cancel, and the caller can decide whether
            // this was intentional.
            if (headerBytesRead == 0)
            {
                return new NodeShutdown(NodeShutdownReason.ConnectionFailed);
            }
            else if (headerBytesRead != HeaderLength)
            {
                throw new IOException($"Incomplete header read.  {headerBytesRead} of {HeaderLength} bytes read.");
            }

#if TASKHOST
            int packetLength = BitConverter.ToInt32(_headerData, 1);
#else
            int packetLength = BinaryPrimitives.ReadInt32LittleEndian(new Span<byte>(_headerData, 1, 4));
            MSBuildEventSource.Log.PacketReadSize(packetLength);
#endif

            // Read the packet. Set the buffer length now to avoid additional resizing during the read.
            _readBuffer.Position = 0;
            _readBuffer.SetLength(packetLength);
            int packetBytesRead = Read(_readBuffer.GetBuffer(), packetLength);

            if (packetBytesRead < packetLength)
            {
                throw new IOException($"Incomplete packet read. {packetBytesRead} of {packetLength} bytes read.");
            }

            return DeserializePacket();
        }

        private int Read(byte[] buffer, int bytesToRead)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < bytesToRead)
            {
                int bytesRead = NodeStream.Read(buffer, totalBytesRead, bytesToRead - totalBytesRead);

                // 0 byte read will occur if the pipe disconnects.
                if (bytesRead == 0)
                {
                    break;
                }

                totalBytesRead += bytesRead;
            }

            return totalBytesRead;
        }

#if !TASKHOST
        internal async Task<INodePacket> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            // Read the header.
            int headerBytesRead = await ReadAsync(_headerData, HeaderLength, cancellationToken).ConfigureAwait(false);

            // When an active connection is broken, any pending read will return 0 bytes before the pipe transitions to
            // the broken state. As this is expected behavior, don't throw an exception if no packet is pending, A node
            // may disconnect without waiting on the other end to gracefully cancel, and the caller can decide whether
            // this was intentional.
            if (headerBytesRead == 0)
            {
                return new NodeShutdown(NodeShutdownReason.ConnectionFailed);
            }
            else if (headerBytesRead != HeaderLength)
            {
                throw new IOException($"Incomplete header read.  {headerBytesRead} of {HeaderLength} bytes read.");
            }

            int packetLength = BinaryPrimitives.ReadInt32LittleEndian(new Span<byte>(_headerData, 1, 4));
            MSBuildEventSource.Log.PacketReadSize(packetLength);

            // Read the packet. Set the buffer length now to avoid additional resizing during the read.
            _readBuffer.Position = 0;
            _readBuffer.SetLength(packetLength);
            int packetBytesRead = await ReadAsync(_readBuffer.GetBuffer(), packetLength, cancellationToken).ConfigureAwait(false);

            if (packetBytesRead < packetLength)
            {
                throw new IOException($"Incomplete packet read. {packetBytesRead} of {packetLength} bytes read.");
            }

            return DeserializePacket();
        }

        private async Task<int> ReadAsync(byte[] buffer, int bytesToRead, CancellationToken cancellationToken)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < bytesToRead)
            {
#if NETCOREAPP
                int bytesRead = await NodeStream.ReadAsync(buffer.AsMemory(totalBytesRead, bytesToRead - totalBytesRead), cancellationToken).ConfigureAwait(false);
#else
                int bytesRead = await NodeStream.ReadAsync(buffer, totalBytesRead, bytesToRead - totalBytesRead, cancellationToken).ConfigureAwait(false);
#endif

                // 0 byte read will occur if the pipe disconnects.
                if (bytesRead == 0)
                {
                    break;
                }

                totalBytesRead += bytesRead;
            }

            return totalBytesRead;
        }
#endif

        private INodePacket DeserializePacket()
        {
            if (_packetFactory == null)
            {
                throw new InternalErrorException("No packet factory is registered for deserialization.");
            }

            NodePacketType packetType = (NodePacketType)_headerData[0];
            try
            {
                return _packetFactory.DeserializePacket(packetType, _readTranslator);
            }
            catch (Exception e) when (e is not InternalErrorException)
            {
                throw new InternalErrorException($"Exception while deserializing packet {packetType}: {e}");
            }
        }
    }
}
