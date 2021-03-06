﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Sledge.FileSystem;
using Sledge.Settings.Models;

namespace Sledge.Editor.Environment
{
    public class GameEnvironment
    {
        public Game Game { get; private set; }
        private IFile _root;

        public IFile Root
        {
            get
            {
                if (_root == null)
                {
                    var dirs = GetGameDirectories().Where(Directory.Exists).ToList();
                    if (dirs.Any()) _root = new RootFile(Game.Name, dirs.Select(x => new NativeFile(x)));
                    else _root = new VirtualFile(null, "");
                }
                return _root;
            }
        }

        public GameEnvironment(Game game)
        {
            Game = game;
        }

        public IFile GetEditorRoot()
        {
            // Add the editor location to the path, for sprites and the like
            var dirs = GetGameDirectories().ToList();
            dirs.Add(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            dirs.RemoveAll(x => !Directory.Exists(x));

            if (dirs.Any()) return new RootFile(Game.Name, dirs.Select(x => new NativeFile(x)));
            return new VirtualFile(null, "");
        }

        public IEnumerable<string> GetGameDirectories()
        {
            if (Game.SteamInstall)
            {
                // SteamPipe folders: game_addon (custom content), game_downloads (downloaded content)
                yield return Path.Combine(Sledge.Settings.Steam.SteamDirectory, "steamapps", "common", Game.SteamGameDir, Game.ModDir + "_addon");
                yield return Path.Combine(Sledge.Settings.Steam.SteamDirectory, "steamapps", "common", Game.SteamGameDir, Game.ModDir + "_downloads");

                // game_hd (high definition content)
                if (Game.UseHDModels)
                {
                    yield return Path.Combine(Sledge.Settings.Steam.SteamDirectory, "steamapps", "common", Game.SteamGameDir, Game.ModDir + "_hd");
                }

                // Mod and game folders
                yield return Path.Combine(Sledge.Settings.Steam.SteamDirectory, "steamapps", "common", Game.SteamGameDir, Game.ModDir);
                if (!String.Equals(Game.BaseDir, Game.ModDir, StringComparison.CurrentCultureIgnoreCase))
                {
                    // Do the  SteamPipe folders need to be included here too? Possbly not...
                    yield return Path.Combine(Sledge.Settings.Steam.SteamDirectory, "steamapps", "common", Game.SteamGameDir, Game.BaseDir);
                }
            }
            else
            {
                yield return Path.Combine(Game.WonGameDir, Game.ModDir);
                if (!String.Equals(Game.BaseDir, Game.ModDir, StringComparison.CurrentCultureIgnoreCase))
                {
                    yield return Path.Combine(Game.WonGameDir, Game.BaseDir);
                }
            }
        }
    }
}
