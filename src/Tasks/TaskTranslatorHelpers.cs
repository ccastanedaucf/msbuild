// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.Versioning;
using Microsoft.Build.BackEnd;

#nullable disable

namespace Microsoft.Build.Tasks
{
    internal static class TaskTranslatorHelpers
    {
        public static void Translate(this ITranslator translator, ref FrameworkName frameworkName)
        {
            if (!translator.TranslateNullable(frameworkName))
            {
                return;
            }

            string identifier = null;
            Version version = null;
            string profile = null;

            if (translator.Mode == TranslationDirection.WriteToStream)
            {
                identifier = frameworkName.Identifier;
                version = frameworkName.Version;
                profile = frameworkName.Profile;
            }

            translator.Intern(ref identifier, nullable: true);
            translator.Translate(ref version);
            translator.Intern(ref profile, nullable: true);

            if (translator.Mode == TranslationDirection.ReadFromStream)
            {
                frameworkName = new FrameworkName(identifier, version, profile);
            }
        }
    }
}
