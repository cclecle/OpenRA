#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using OpenRA.Network;

namespace OpenRA.Server
{
	sealed class Program
	{
		static void Main(string[] args)
		{
			try
			{
				Run(args);
			}
			finally
			{
				Log.Dispose();
			}
		}

		static void Run(string[] args)
		{
			var arguments = new Arguments(args);

			var engineDirArg = arguments.GetValue("Engine.EngineDir", null);
			if (!string.IsNullOrEmpty(engineDirArg))
				Platform.OverrideEngineDir(engineDirArg);

			var supportDirArg = arguments.GetValue("Engine.SupportDir", null);
			if (!string.IsNullOrEmpty(supportDirArg))
				Platform.OverrideSupportDir(supportDirArg);

			Log.AddChannel("server", "dedicated-server.log", true);
			Log.AddChannel("perf", "dedicated-perf.log", true);
			Log.AddChannel("debug", "dedicated-debug.log", true);
			Log.AddChannel("nat", "dedicated-nat.log", true);
			Log.AddChannel("geoip", "dedicated-geoip.log", true);

			// Special case handling of Game.Mod argument: if it matches a real filesystem path
			// then we use this to override the mod search path, and replace it with the mod id
			var modID = arguments.GetValue("Game.Mod", null);
			var explicitModPaths = Array.Empty<string>();
			if (modID != null && (File.Exists(modID) || Directory.Exists(modID)))
			{
				explicitModPaths = new[] { modID };
				modID = Path.GetFileNameWithoutExtension(modID);
			}

			if (modID == null)
				throw new InvalidOperationException("Game.Mod argument missing or mod could not be found.");

			// HACK: The engine code assumes that Game.Settings is set.
			// This isn't nearly as bad as ModData, but is still not very nice.
			Game.InitializeSettings(arguments);
			var settings = Game.Settings.Server;
			Game.HeadLess = true;
			var platform = Game.CreatePlatform(Game.Settings.Game.Platform);
			Game.Sound = new Sound(platform, Game.Settings.Sound);

			Nat.Initialize();

			var envModSearchPaths = Environment.GetEnvironmentVariable("MOD_SEARCH_PATHS");
			var modSearchPaths = !string.IsNullOrWhiteSpace(envModSearchPaths) ?
				FieldLoader.GetValue<string[]>("MOD_SEARCH_PATHS", envModSearchPaths) :
				new[] { Path.Combine(Platform.EngineDir, "mods") };

			var mods = new InstalledMods(modSearchPaths, explicitModPaths);

			WriteLineWithTimeStamp($"Starting dedicated server for mod: {modID}");
			while (true)
			{
				// HACK: The engine code *still* assumes that Game.ModData is set
				var modData = Game.ModData = new ModData(mods[modID], mods);
				modData.MapCache.LoadPreviewImages = false; // PERF: Server doesn't need previews, save memory by not loading them.
				modData.MapCache.LoadMaps();

				// HACK: Related to the above one, initialize the translations so we can load maps with their (translated) lobby options.
				TranslationProvider.Initialize(modData, modData.DefaultFileSystem);

				var endpoints = new List<IPEndPoint> { new(IPAddress.IPv6Any, settings.ListenPort), new(IPAddress.Any, settings.ListenPort) };
				var server = new Server(endpoints, settings, modData, ServerType.Dedicated);

				GC.Collect();

				var watch = Stopwatch.StartNew();
				while ((server.State != ServerState.WaitingPlayers) && (watch.ElapsedMilliseconds < 30000)) { Thread.Sleep(1000); }
				if (watch.ElapsedMilliseconds >= 30000) throw new Exception("Server Start Error.");

				while (server.State == ServerState.WaitingPlayers) { Thread.Sleep(1000); }

				while (server.State == ServerState.GameStarted)
				{
					Thread.Sleep(1000);

					if (server.State == ServerState.GameStarted && server.LobbyInfo.NonBotClients.ToArray().Length == 0)
					{
						WriteLineWithTimeStamp("No one is playing, shutting down...");
						server.Shutdown();
						break;
					}
				}

				WriteLineWithTimeStamp("Server exited.");

				watch = Stopwatch.StartNew();
				while (((server.State != ServerState.Stopped) || (Game.State == RunStatus.Running)) && (watch.ElapsedMilliseconds < 10000)) { Thread.Sleep(1000); }
				if (watch.ElapsedMilliseconds >= 30000) throw new Exception("Server Stop Error.");

				modData.Dispose();
				WriteLineWithTimeStamp("Starting a new server instance...");
			}
		}

		static void WriteLineWithTimeStamp(string line)
		{
			Console.WriteLine($"[{DateTime.Now.ToString(Game.Settings.Server.TimestampFormat, CultureInfo.CurrentCulture)}] {line}");
		}
	}
}
