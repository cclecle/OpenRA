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

/*
	Query server implementation compliant with: https://developer.valvesoftware.com/wiki/Server_queries

	Implementing Source Protocol, with some implementation specific limitations / behaviors:
	- no compression support
	- <Size> Field in Multi-packet Response is not supported
	- server will request challenge for every new session
	Tested using Qstat.exe (for standard fields)
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Caching;
using OpenRA.Graphics;
using OpenRA.QueryStats;
using OpenRA.Traits;

namespace OpenRA.Server
{
	public static class QueryStatUtils
	{
		public static NumberFormatInfo Nfi = new() { NumberDecimalSeparator = "." };
	}

	#region Player stats update
	[TraitLocation(SystemActors.World)]
	[Desc("Attach this to the Word to collect observer stats.")]
	public class QueryStatsUpdatePlayerStatsInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new QueryStatsUpdatePlayerStats(init.World); }
	}

	public class QueryStatsUpdatePlayerStats : Traits.ITick, IIsServerOnly, INotifyCreated, IWorldLoaded
	{
		readonly World world;
		readonly GameStats<PlayerStats> gameStats;

		public QueryStatsUpdatePlayerStats(World world)
		{
			this.world = world;
			gameStats = Game.Server?
							.InstQueryStatStatServer
							.StatsSessionHander
							.GameStats;
		}

		void INotifyCreated.Created(Actor self)
		{
			Console.WriteLine($"QueryStatsUpdatePlayerStats.Created() {world} :)");
		}

		int ticks;
		readonly IDictionary<Player, (IPlayerQueryStatsUpdate InternalPStat, PlayerStats QueryPStat)> playersTraits = new Dictionary<Player, (IPlayerQueryStatsUpdate InternalPStat, PlayerStats QueryPStat)>();
		public void WorldLoaded(World w, WorldRenderer wr)
		{
			foreach (var p in w.Players.Where(p => p.SpawnPoint != 0))
			{
				var internal_pstat = p.PlayerActor.TraitsImplementing<IPlayerQueryStatsUpdate>().First();
				var query_pstat = gameStats.Players.First(pstat => pstat.Index == p.ClientIndex);
				playersTraits.Add(p, (internal_pstat, query_pstat));
			}
		}

		void Traits.ITick.Tick(Actor self)
		{
			ticks++;

			var timestep = self.World.Timestep;
			if (ticks * timestep >= 2000)
			{
				Console.WriteLine("QueryStatsUpdatePlayerStats.Tick()");
				ticks = 0;

				foreach (var pt in playersTraits)
				{
					var internal_pstats = pt.Value.InternalPStat.PlayerStatsUpdate();
					lock (gameStats)
					{
						var query_pstats = pt.Value.QueryPStat;
						query_pstats.Name = pt.Key.PlayerName;
						query_pstats.Experience = internal_pstats.Experience;
						query_pstats.Team = pt.Key.PlayerReference.Team;
						query_pstats.ArmyValue = internal_pstats.ArmyValue;
						query_pstats.AssetsValue = internal_pstats.AssetsValue;
						query_pstats.BuildingsDead = internal_pstats.BuildingsDead;
						query_pstats.BuildingsKilled = internal_pstats.BuildingsKilled;
						query_pstats.Earned = internal_pstats.Earned;
						query_pstats.UnitsDead = internal_pstats.UnitsDead;
						query_pstats.UnitsKilled = internal_pstats.UnitsKilled;
					}
				}
			}
		}
	}
	#endregion

	#region Server stats update
	public class QueryStatUpdateTrait : INotifySyncLobbyInfo, IStartGame, IEndGame, IClientJoined, INotifyServerStart, INotifyServerEmpty, INotifyServerShutdown, ITick
	{
		readonly GameStats<PlayerStats> gameStats;

		public QueryStatUpdateTrait(Server server)
		{
			gameStats = server.InstQueryStatStatServer
					.StatsSessionHander
					.GameStats;
		}

		public void ServerStarted(Server server)
		{
			if (server.GameInfo is null)
				gameStats.StartTimeUtc = DateTime.UtcNow;
			FullUpdate(server);
		}

		public void ServerEmpty(Server server) => FullUpdate(server);
		public void ServerShutdown(Server server) => FullUpdate(server);
		public void ClientJoined(Server server, Connection conn) => FullUpdate(server);
		public void GameEnded(Server server) => FullUpdate(server);
		public void GameStarted(Server server) => FullUpdate(server);
		public void LobbyInfoSynced(Server server) => FullUpdate(server);

		public void FullUpdate(Server server)
		{
			lock (gameStats)
			{
				gameStats.AdvertiseOnline = server
											.Settings
											.AdvertiseOnline;
				gameStats.PasswordProtected = server
												.Settings
												.Password
												.Length > 0;
				gameStats.RequireAuthentication = server
													.Settings
													.RequireAuthentication;

				gameStats.AllowSpectators = server
											.LobbyInfo
											.GlobalSettings
											.AllowSpectators;

				gameStats.EnableVoteKick = server
											.Settings
											.EnableVoteKick;

				gameStats.ListenPort = server.Settings.ListenPort;
				gameStats.SType = server.Type;
				gameStats.SState = server.State;
				gameStats.SName = server.LobbyInfo.GlobalSettings.ServerName;

				var gameSpeeds = Game.ModData.Manifest.Get<GameSpeeds>();
				gameStats.GameSpeedName = server.LobbyInfo.GlobalSettings.OptionOrDefault("gamespeed", gameSpeeds.DefaultSpeed);

				var shroudInfo = Game.ModData.DefaultRules.Actors[SystemActors.Player].TraitInfo<ShroudInfo>();
				gameStats.ExploreMapEnabled = server.LobbyInfo.GlobalSettings.OptionOrDefault("explored", shroudInfo.ExploredMapCheckboxEnabled);
				gameStats.FogEnabled = server.LobbyInfo.GlobalSettings.OptionOrDefault("fog", shroudInfo.FogCheckboxEnabled);

				gameStats.OtherRules.Clear();
				if (server.Map is not null)
				{
					gameStats.Map = server.Map.Title;
					gameStats.MaxPlayer = server.Map.SpawnPoints.Length;

					foreach (var t in server.Map.WorldActorInfo.TraitInfos<ITraitInfoQueryStatRules>())
					{
						foreach (var r in t.GetRules(server.LobbyInfo))
							gameStats.OtherRules.Add(r);
					}

					foreach (var t in server.Map.PlayerActorInfo.TraitInfos<ITraitInfoQueryStatRules>())
					{
						foreach (var r in t.GetRules(server.LobbyInfo))
							gameStats.OtherRules.Add(r);
					}
				}

				gameStats.MapID = server.LobbyInfo.GlobalSettings.Map ?? "";

				if (server.GameInfo is not null)
					gameStats.StartTimeUtc = server.GameInfo.StartTimeUtc;

				gameStats.Players.Clear();

				foreach (var c in server.LobbyInfo.Clients.Where(c => !c.IsHiddenObserver))
				{
					gameStats.Players.Add(new PlayerStats()
					{
						Index = c.Index,
						Faction = c.Faction,
						IsAdmin = c.IsAdmin,
						IsBot = c.IsBot,
						IsSectating = c.IsObserver,
						Name = c.Name,
						Team = c.Team,
						Experience = 0,
						ArmyValue = 0,
						AssetsValue = 0,
						BuildingsDead = 0,
						BuildingsKilled = 0,
						Earned = 0,
						UnitsDead = 0,
						UnitsKilled = 0,
						RemoteEndPoint = c.IPAddress ?? "",
						LastLatency = c.LastLatency,
					});
				}
			}
		}

		int ticks = 0;
		void ITick.Tick(Server server)
		{
			ticks++;

			if (ticks >= 50)
			{
				ticks = 0;
				foreach (var c in server.LobbyInfo.Clients.Where(c => !c.IsHiddenObserver))
				{
					var pstat = gameStats.Players.First(p => p.Index == c.Index);
					pstat.LastLatency = c.LastLatency;
				}
			}
		}
	}
	#endregion

	#region Internal stats data structures
	public class PlayerStats
	{
		public int Index;
		public string Faction = "";
		public bool IsAdmin = false;
		public bool IsBot = false;
		public bool IsSectating = false;
		public string Name = "";
		public int Team = -1;
		public int Experience = -1;
		public int ArmyValue = -1;
		public int AssetsValue = -1;
		public int BuildingsDead = -1;
		public int BuildingsKilled = -1;
		public int Earned = -1;
		public int UnitsDead = -1;
		public int UnitsKilled = -1;
		public string RemoteEndPoint = "";
		public int LastLatency = -1;
	}

	public class GameStats<TPlayerStats> where TPlayerStats : PlayerStats, new()
	{
		public string GameSpeedName;
		public bool ExploreMapEnabled;
		public bool FogEnabled;
		public DateTime StartTimeUtc = DateTime.Now;
		public PlatformType RunningPlatform = Platform.CurrentPlatform;
		public string SVersion = Game.ModData.Manifest.Metadata.Version;
		public ServerType? SType = null;
		public ServerState? SState = null;
		public string SName = "";
		public string Map = "";
		public string MapID = "";
		public string Mod = Game.ModData.Manifest.Id;
		public bool RequireAuthentication = false;
		public bool AllowSpectators = false;
		public bool AdvertiseOnline = true;
		public bool PasswordProtected = false;
		public bool EnableVoteKick = false;
		public int MaxPlayer = -1;
		public ICollection<TPlayerStats> Players = new List<TPlayerStats>();
		public int ListenPort = -1;
		public ICollection<Tuple<string, string>> OtherRules = new List<Tuple<string, string>>();
	}
	#endregion

	#region Query Protocol Server & Session classes buildup
	public class OpenRAServerSessionHandler<TGameStats, TPlayerStats> : ServerSessionHandler<OpenRAServerSession<TGameStats, TPlayerStats>>
	where TGameStats : GameStats<TPlayerStats>, new()
	where TPlayerStats : PlayerStats, new()
	{
		public readonly TGameStats GameStats = new();
		protected override OpenRAServerSession<TGameStats, TPlayerStats> CreateSession()
		{
			var session = base.CreateSession();
			session.GameStats = GameStats;
			return session;
		}
	}

	public class OpenRAServerSession<TGameStats, TPlayerStats> : ServerSession
	where TGameStats : GameStats<TPlayerStats>, new()
	where TPlayerStats : PlayerStats, new()
	{
		public TGameStats GameStats = null;
		static readonly ObjectCache Cache = MemoryCache.Default;

		public OpenRAServerSession()
		{
			MessageFactory.GetFactory().CustomMessages.Add(new A2S_PLAYER_EX());
			MessageFactory.GetFactory().CustomMessages.Add(new S2A_PLAYER_EX());
		}

		protected override Frame OnMessageReceived(Message message)
		{
			Console.WriteLine("decoded msg:");
			Console.WriteLine(message);
			return base.OnMessageReceived(message);
		}

		protected override Frame ProcessCustomCommand(A2S_SimpleCommand a2S_SimpleCommand)
		{
			if (a2S_SimpleCommand is A2S_PLAYER_EX a2S_PLAYER_EX)
			{
				return Impl_Process_A2S_PLAYER_EX(a2S_PLAYER_EX);
			}

			return null;
		}

		protected override Frame Impl_Process_A2S_INFO(A2S_INFO a2S_INFO)
		{
			Frame s2aInfo;

			if (!Cache.Contains("S2A_INFO"))
			{
				lock (GameStats)
				{
					var nbots = (byte)GameStats.Players.Count(p => p.IsBot);
					var stype = GameStats.SType == ServerType.Dedicated ? 'd' : 'i';

					var environment = 'u';
					switch (GameStats.RunningPlatform)
					{
						case PlatformType.Windows:
							environment = 'w';
							break;
						case PlatformType.OSX:
							environment = 'm';
							break;
						case PlatformType.Linux:
							environment = 'l';
							break;
					}

					s2aInfo = new Frame(new S2A_INFO
					{
						Name = GameStats.SName,
						Map = GameStats.Map,
						Folder = "",
						Game = GameStats.Mod,
						ID = 0x0000,
						Players = (byte)GameStats.Players.Count,
						MaxPlayers = (byte)GameStats.MaxPlayer,
						Bots = nbots,
						ServerType = (byte)stype,
						Environment = (byte)environment,
						Visibility = GameStats.AdvertiseOnline ? (byte)0 : (byte)1,
						VAC = 0,
						Version = GameStats.SVersion,
						Port = (short)GameStats.ListenPort,
					});
				}

				Cache.Add("S2A_INFO", s2aInfo, DateTimeOffset.UtcNow.AddSeconds(5));
			}
			else
			{
				s2aInfo = (Frame)Cache.Get("S2A_INFO");
			}

			return s2aInfo;
		}

		protected override Frame Impl_Process_A2S_RULES(A2S_RULES a2S_RULES)
		{
			Frame s2aRules;

			if (!Cache.Contains("S2A_RULES"))
			{
				var msg = new S2A_RULES();

				lock (GameStats)
				{
					msg.Rules.Add(new S2A_RULE_DATA { Name = "ServerState", Value = GameStats.SState.ToString() });
					msg.Rules.Add(new S2A_RULE_DATA { Name = "RequireAuthentication", Value = GameStats.RequireAuthentication.ToString() });
					msg.Rules.Add(new S2A_RULE_DATA { Name = "PasswordProtected", Value = GameStats.PasswordProtected.ToString() });
					msg.Rules.Add(new S2A_RULE_DATA { Name = "EnableVoteKick", Value = GameStats.EnableVoteKick.ToString() });
					msg.Rules.Add(new S2A_RULE_DATA { Name = "MapID", Value = GameStats.MapID });
					msg.Rules.Add(new S2A_RULE_DATA { Name = "Duration", Value = $"{(DateTime.UtcNow - GameStats.StartTimeUtc).TotalSeconds.ToString(QueryStatUtils.Nfi)}" });
					msg.Rules.Add(new S2A_RULE_DATA { Name = "GameSpeed", Value = GameStats.GameSpeedName });
					msg.Rules.Add(new S2A_RULE_DATA { Name = "ExploreMapEnabled", Value = GameStats.ExploreMapEnabled.ToString() });
					msg.Rules.Add(new S2A_RULE_DATA { Name = "FogEnabled", Value = GameStats.FogEnabled.ToString() });
					msg.Rules.Add(new S2A_RULE_DATA { Name = "NumSpectators", Value = $"{GameStats.Players.Count(p => p.IsSectating)}" });
					msg.Rules.Add(new S2A_RULE_DATA { Name = "AllowSpectators", Value = GameStats.AllowSpectators.ToString() });
					foreach (var r in GameStats.OtherRules)
						msg.Rules.Add(new S2A_RULE_DATA { Name = r.Item1, Value = r.Item2 });
				}

				s2aRules = new Frame(msg);
				Cache.Add("S2A_RULES", s2aRules, DateTimeOffset.UtcNow.AddSeconds(5));
			}
			else
			{
				s2aRules = (Frame)Cache.Get("S2A_RULES");
			}

			return s2aRules;
		}

		protected override Frame Impl_Process_A2S_PLAYER(A2S_PLAYER aA2S_PLAYER)
		{
			Frame s2aPlayer;

			if (!Cache.Contains("S2A_PLAYER"))
			{
				var now = DateTime.UtcNow;
				var msg = new S2A_PLAYER();

				lock (GameStats)
				{
					foreach (var player in GameStats.Players)
					{
						var pname = player.IsSectating ? $"[SPEC] {player.Name}" : player.Name;
						msg.Players.Add(new S2A_PLAYER_DATA { Index = (byte)player.Index, PlayerName = pname, Score = player.Experience, Duration = (float)(now - GameStats.StartTimeUtc).TotalSeconds });
					}
				}

				s2aPlayer = new Frame(msg);
				Cache.Add("S2A_PLAYER", s2aPlayer, DateTimeOffset.UtcNow.AddSeconds(5));
			}
			else
			{
				s2aPlayer = (Frame)Cache.Get("S2A_PLAYER");
			}

			return s2aPlayer;
		}

		protected Frame Impl_Process_A2S_PLAYER_EX(A2S_PLAYER_EX aA2S_PLAYER_EX)
		{
			Frame s2aPlayerEx;

			if (!Cache.Contains("S2A_PLAYER_EX"))
			{
				var msg = new S2A_PLAYER_EX();
				var now = DateTime.UtcNow;

				lock (GameStats)
				{
					foreach (var player in GameStats.Players)
					{
						var player_ex_data = new S2A_PLAYER_EX_DATA() { PlayerIndex = (byte)player.Index };
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__IsInTeam() { AttrValue = player.Team > 0 ? (byte)1 : (byte)0 });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__TeamId() { AttrValue = (byte)player.Team });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__Name() { AttrValue = player.Name });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__Score() { AttrValue = player.Experience });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__ConnectionTime() { AttrValue = (float)(now - GameStats.StartTimeUtc).TotalSeconds });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__Faction() { AttrValue = player.Faction });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__IsAdmin() { AttrValue = player.IsAdmin ? (byte)1 : (byte)0 });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__IsBot() { AttrValue = player.IsBot ? (byte)1 : (byte)0 });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__IsSpectating() { AttrValue = player.IsSectating ? (byte)1 : (byte)0 });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__ArmyValue() { AttrValue = player.ArmyValue });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__AssetsValue() { AttrValue = player.AssetsValue });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__BuildingsDead() { AttrValue = player.BuildingsDead });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__BuildingsKilled() { AttrValue = player.BuildingsKilled });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__Earned() { AttrValue = player.Earned });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__UnitsDead() { AttrValue = player.UnitsDead });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__UnitsKilled() { AttrValue = player.UnitsKilled });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__IP() { AttrValue = player.RemoteEndPoint });
						player_ex_data.PlayerInfo.Add(new S2A_PLAYER_EX_DATA_FIELD__Ping() { AttrValue = (short)player.LastLatency });
						msg.Players.Add(player_ex_data);
					}
				}

				s2aPlayerEx = new Frame(msg);
				Cache.Add("S2A_PLAYER_EX", s2aPlayerEx, DateTimeOffset.UtcNow.AddSeconds(5));
			}
			else
			{
				s2aPlayerEx = (Frame)Cache.Get("S2A_PLAYER_EX");
			}

			return s2aPlayerEx;
		}
	}
	#endregion

	#region UDP Async server

	public sealed class QueryStatStatServer<TGameStats, TPlayerStats> : IDisposable
	where TGameStats : GameStats<TPlayerStats>, new()
	where TPlayerStats : PlayerStats, new()
	{
		static QueryStatStatServer<TGameStats, TPlayerStats> instance;
		static readonly PacketFactory PktFactory = PacketFactory.GetFactory();
		UdpClient udpClient;
		public readonly int ListenPort;
		bool cancelReceive;
		public readonly OpenRAServerSessionHandler<TGameStats, TPlayerStats> StatsSessionHander;

		public static QueryStatStatServer<TGameStats, TPlayerStats> GetServer(Server parrentServer, int listenPort = 27600)
		{
			instance ??= new QueryStatStatServer<TGameStats, TPlayerStats>(listenPort);
			return instance;
		}

#pragma warning disable IDE0040 //allow private constructor to implement singleton pattern
		private QueryStatStatServer(int listenPort)
#pragma warning restore IDE0040
		{
			ListenPort = listenPort;
			StatsSessionHander = new OpenRAServerSessionHandler<TGameStats, TPlayerStats>();
		}

		public void StartServer()
		{
			if (udpClient != null) throw new Exception("QueryStatStatServer is already started");
			udpClient = new UdpClient(11000);
			cancelReceive = false;
			udpClient.BeginReceive(new AsyncCallback(QueryReceiveCallback), null);
		}

		public void StopServer()
		{
			if (udpClient is null) throw new Exception("QueryStatStatServer is not started");
			cancelReceive = true;
			CloseServer();
		}

		void CloseServer()
		{
			if (udpClient != null)
			{
				udpClient.Close();
				udpClient = null;
			}
		}

		void QueryReceiveCallback(IAsyncResult ar)
		{
			IPEndPoint remoteIpEndPoint = null;

			if (cancelReceive) return;

			var receivedData = udpClient.EndReceive(ar, ref remoteIpEndPoint);
			udpClient.BeginReceive(new AsyncCallback(QueryReceiveCallback), null);
			if (receivedData.Length > 0)
			{
				Console.WriteLine("=====================================");
				Console.WriteLine($"Received Data: {BitConverter.ToString(receivedData)}");
				try
				{
					var pkt = PktFactory.GetMessage(new MemoryStream(receivedData));
					if (pkt is Packet packet)
					{
						Console.WriteLine("decoded pkt:");
						Console.WriteLine(pkt);
						var resp_frame = StatsSessionHander.ConsumePacket(remoteIpEndPoint, packet);
						Console.WriteLine("response msg:");
						Console.WriteLine(resp_frame.Msg);
						foreach (var resp_pkt in resp_frame)
						{
							var data = resp_pkt.Serialize().ToArray();
							udpClient.Send(data, data.Length, remoteIpEndPoint);
						}
					}
				}
				catch (Exception e)
				{
					Console.WriteLine("Wrong Stat Message Received");
					Console.WriteLine(BitConverter.ToString(receivedData));
					Console.WriteLine("Details:");
					Console.WriteLine(e);
					Log.Write("debug", "Wrong Stat Message Received");
					Log.Write("debug", BitConverter.ToString(receivedData));
					Log.Write("debug", "Details:");
					Log.Write("debug", e);
				}
			}
		}

		public void Dispose()
		{
			CloseServer();
		}
	}
	#endregion
}
