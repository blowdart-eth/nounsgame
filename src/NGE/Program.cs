﻿using NGE.Core.Configuration;
using NGE.Graphics;

namespace NGE
{
    // ReSharper disable once UnusedMember.Global
    internal static class Program
    {
        public static void Main(params string[] args)
        {
            Graphics.Bootstrap.Init();
            var configuration = Config.GetOrCreateConfiguration();
            using var game = new NounsGame(configuration, args);
            game.Run();
        }
    }
}
