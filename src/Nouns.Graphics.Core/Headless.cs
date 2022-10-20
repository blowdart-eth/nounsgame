﻿using System;
using System.ComponentModel.Design;
using Microsoft.Xna.Framework.Graphics;
using Nouns.Assets.Core;

namespace Nouns.Graphics.Core
{
    public sealed class Headless : IGraphicsDeviceService
    {
        public GraphicsDevice GraphicsDevice { get; }

        public event EventHandler<EventArgs> DeviceCreated = (_, _) => { };
        public event EventHandler<EventArgs> DeviceDisposing = (_, _) => { };
        public event EventHandler<EventArgs> DeviceReset = (_, _) => { };
        public event EventHandler<EventArgs> DeviceResetting = (_, _) => { };

        public Headless(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;
        }

        private static string rootDirectory;
        private static GraphicsDevice graphicsDevice;
        private static IServiceContainer services;
        private static AssetManager assetPathProvider;

        public static AssetManager AcquireAssetManager(string directory = null)
        {
            directory ??= Environment.CurrentDirectory;

            if (rootDirectory != directory)
                assetPathProvider = null;

            if (assetPathProvider != null)
                return assetPathProvider;

            assetPathProvider = new AssetManager(AcquireServices(), directory);
            rootDirectory = directory;
            return assetPathProvider;
        }

        public static IServiceContainer AcquireServices()
        {
            if (services == null)
                AcquireGraphicsDevice();

            return services;
        }

        public static GraphicsDevice AcquireGraphicsDevice()
        {
            Bootstrap.Init();

            if (graphicsDevice == null)
            {
                var game = new NoGame();
                game.RunOneFrame();
                services = new ServiceContainer();
                services.AddService(typeof(IGraphicsDeviceService), new Headless(game.GraphicsDevice));
                graphicsDevice = game.GraphicsDevice;
            }

            return graphicsDevice;
        }
    }
}