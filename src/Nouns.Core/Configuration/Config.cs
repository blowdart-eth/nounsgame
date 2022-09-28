﻿using Microsoft.Extensions.Configuration;
using Tomlyn.Syntax;

namespace Nouns.Core.Configuration
{
    public static class Config
    {
        private static IConfigurationRoot configuration = null!;
        private static string configFilePath = null!;

        public static IConfiguration GetOrCreateConfiguration(string configFileName = Constants.DefaultConfigFileName)
        {
            var workingDir = Directory.GetCurrentDirectory();
            configFilePath = Path.Combine(workingDir, configFileName);

            if (!File.Exists(configFilePath))
                CreateDefaultConfigFile();

            var builder = new ConfigurationBuilder()
                .SetBasePath(workingDir)
                .AddTomlFile(configFilePath, optional: false, reloadOnChange: true);

            configuration = builder.Build();
            return configuration;
        }

        private static void CreateDefaultConfigFile()
        {
            var document = new DocumentSyntax
            {
                Tables =
                {
                    new TableSyntax("web3")
                    {
                        Items =
                        {
                            {"rpcUrl", "http://localhost:8545"}
                        }
                    },
                    new TableArraySyntax("web3.knownContracts")
                    {
                        // ReSharper disable StringLiteralTypo
                        // ReSharper disable once CommentTypo

                        Items =
                        {
                            {"Nouns", @"0x9c8ff314c9bc7f6e59a9d9225fb22946427edc03"},
                            {"CrypToadz", @"0x1cb1a5e65610aeff2551a50f76a87a7d3fb649c6"},
                            // {"Terraforms", @"0x4e1f41613c9084fdb9e34e11fae9412427480e56"}
                        }
                    },
                    new TableSyntax("locations")
                    {
                        Items =
                        {
                            {"assetDirectory", Directory.GetCurrentDirectory()},
                        }
                    }
                }
            };

            Save(document);
        }

        private static void Save(SyntaxNode document)
        {
            using var fs = File.OpenWrite(configFilePath);
            using var sw = new StreamWriter(fs);
            document.WriteTo(sw);
        }
    }
}