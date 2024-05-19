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
using System.IO;
using System.Linq;
using System.Net;
using NUnit.Framework;
using OpenRA.QueryStats;

namespace OpenRA.Test
{
	#region TestDefs
	class TestClientSession : ClientSession
	{
		public int? GetChallenge() => challenge;
		public bool OnCommandCompletedCalled = false;
		protected override void OnCommandCompleted(A2S_SimpleCommand command) => OnCommandCompletedCalled = true;
	}

	public class TestException : Exception { }
	class TestServerSession_Payload : ServerSession
	{
		protected override Frame Impl_Process_A2S_INFO(A2S_INFO a2S_INFO)
		{
			if (a2S_INFO.Payload != "TEST CHECK")
				throw new TestException();
			return new Frame(new S2A_INFO());
		}

		protected override Frame Impl_Process_A2S_RULES(A2S_RULES a2S_RULES) => null;

		protected override Frame Impl_Process_A2S_PLAYER(A2S_PLAYER aA2S_PLAYER) => null;
	}

	class TestServerSession : ServerSession
	{
		public int? GetChallenge() => challenge;
		public void FakeLastUpdatedDateTime(DateTime newTime) => LastUpdatedDateTime = newTime;
		public void FakeChallengeCreationDateTime(DateTime newTime) => challengeCreationDateTime = newTime;
		protected override Frame Impl_Process_A2S_INFO(A2S_INFO a2S_INFO)
		{
			return new Frame(new S2A_INFO
			{
				Name = "NAME65",
				Map = "MAP14",
				Folder = "FOLDER78",
				Game = "GAME14",
				ID = 0x1564,
				Players = 7,
				MaxPlayers = 8,
				Bots = 96,
				ServerType = (byte)'l',
				Environment = (byte)'m',
				Visibility = 98,
				VAC = 65,
				Version = "2.3.1",
				Port = 0x3a5b,
				SteamID = 0x129875a6b5e5f912,
				SpecPort = 0x5298,
				SpecName = "abcd",
				Keywords = "efgh",
				GameID = 0x095a3b5c3f69e563
			});
		}

		protected override Frame Impl_Process_A2S_RULES(A2S_RULES a2S_RULES)
		{
			var msg = new S2A_RULES();
			for (var i = 0; i < 255; i++)
				msg.Rules.Add(new S2A_RULE_DATA { Name = $"SuperLongAndBoringRuleKeyThatHopefullyWillSplitMsgs_{i}", Value = $"SuperLongAndBoringRuleValueThatHopefullyWillSplitMsgs_{i}" });
			return new Frame(msg);
		}

		protected override Frame Impl_Process_A2S_PLAYER(A2S_PLAYER aA2S_PLAYER)
		{
			var msg = new S2A_PLAYER();
			for (var i = 0; i < 255; i++)
				msg.Players.Add(new S2A_PLAYER_DATA { Index = (byte)i, PlayerName = $"SuperLongAndBoringPlayerNameThatHopefullyWillSplitMsgs_{i}", Score = 10 * i, Duration = 100f * i + 50f });
			return new Frame(msg);
		}
	}

	class TestServerSessionHandler : ServerSessionHandler<TestServerSession>
	{
		public IDictionary<IPEndPoint, TestServerSession> GetActiveSessions() => activeSessions;
	}

	class TestClientSessionHandler : ClientSessionHandler<TestClientSession>
	{
		public IDictionary<IPEndPoint, TestClientSession> GetActiveSessions() => activeSessions;
	}

	public class NewCustomMessage : A2S_SimpleCommand
	{
		public override byte Header { get => 0x42; }
	}

	#endregion

	#region SessionHandler
	[TestFixture]
	sealed class QueryStatsTest_SessionHandler
	{
		[SetUp]
		public void TestSetUp()
		{
			MessageFactory.GetFactory().CustomMessages.Clear();
		}

		[TearDown]
		public void TestTearDown()
		{
			MessageFactory.GetFactory().CustomMessages.Clear();
		}

		[Test]
		public void ClientSessionHandler2ServerSessionHandler_S2A_RULES()
		{
			var s1SessionHdlr = new TestServerSessionHandler();
			var s2SessionHdlr = new TestServerSessionHandler();
			var c1SessionHdlr = new TestClientSessionHandler();
			var c2SessionHdlr = new TestClientSessionHandler();
			var iPEndPoint_s1 = IPEndPoint.Parse("1.2.3.4:1234");
			var iPEndPoint_s2 = IPEndPoint.Parse("1.2.3.5:1234");
			var iPEndPoint_c1 = IPEndPoint.Parse("1.2.3.6:1234");
			var iPEndPoint_c2 = IPEndPoint.Parse("1.2.3.7:1234");

			Assert.AreEqual(0, s1SessionHdlr.GetActiveSessions().Count);
			Assert.AreEqual(0, s2SessionHdlr.GetActiveSessions().Count);

			Assert.AreEqual(0, c1SessionHdlr.GetActiveSessions().Count);
			Assert.AreEqual(0, c2SessionHdlr.GetActiveSessions().Count);

			// Simulation 1st client to 1st server Exchange
			var res = ClientSessionHdlr2ServerSessionHdlrSimpleExchange(c1SessionHdlr, iPEndPoint_c1, s1SessionHdlr, iPEndPoint_s1, new A2S_RULES());
			Assert.IsInstanceOf<S2A_RULES>(res);
			var typed_msg = (S2A_RULES)res;
			Assert.AreEqual(typed_msg.Rules.Count, 255);
			for (var i = 0; i < 255; i++)
			{
				Assert.AreEqual($"SuperLongAndBoringRuleValueThatHopefullyWillSplitMsgs_{i}", typed_msg.Rules[i].Value);
				Assert.AreEqual($"SuperLongAndBoringRuleKeyThatHopefullyWillSplitMsgs_{i}", typed_msg.Rules[i].Name);
			}

			Assert.AreEqual(1, c1SessionHdlr.GetActiveSessions().Count);
			Assert.AreEqual(1, s1SessionHdlr.GetActiveSessions().Count);

			// Checking that Challenges has been set
			Assert.NotNull(s1SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge());
			Assert.NotNull(c1SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			Assert.AreEqual(s1SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge(), c1SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			var savedChallengeExch1 = s1SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge();

			// Simulation 2nd client to 1st server Exchange
			var res2 = ClientSessionHdlr2ServerSessionHdlrSimpleExchange(c2SessionHdlr, iPEndPoint_c2, s1SessionHdlr, iPEndPoint_s1, new A2S_RULES());
			Assert.IsInstanceOf<S2A_RULES>(res2);
			var typed_msg2 = (S2A_RULES)res2;
			Assert.AreEqual(typed_msg2.Rules.Count, 255);
			for (var i = 0; i < 255; i++)
			{
				Assert.AreEqual($"SuperLongAndBoringRuleValueThatHopefullyWillSplitMsgs_{i}", typed_msg2.Rules[i].Value);
				Assert.AreEqual($"SuperLongAndBoringRuleKeyThatHopefullyWillSplitMsgs_{i}", typed_msg2.Rules[i].Name);
			}

			Assert.AreEqual(1, c1SessionHdlr.GetActiveSessions().Count);
			Assert.AreEqual(1, c2SessionHdlr.GetActiveSessions().Count);
			Assert.AreEqual(2, s1SessionHdlr.GetActiveSessions().Count);

			// Checking that Challenges has been set
			Assert.NotNull(s1SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge());
			Assert.NotNull(s1SessionHdlr.GetSession(iPEndPoint_c2).GetChallenge());
			Assert.NotNull(c1SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			Assert.NotNull(c2SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			Assert.AreEqual(s1SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge(), c1SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			Assert.AreEqual(s1SessionHdlr.GetSession(iPEndPoint_c2).GetChallenge(), c2SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			var savedChallengeExch2 = s1SessionHdlr.GetSession(iPEndPoint_c2).GetChallenge();
			Assert.AreNotEqual(savedChallengeExch1, savedChallengeExch2);

			// Simulation 1st client to 2nd server Exchange
			var res3 = ClientSessionHdlr2ServerSessionHdlrSimpleExchange(c1SessionHdlr, iPEndPoint_c1, s2SessionHdlr, iPEndPoint_s2, new A2S_RULES());
			Assert.IsInstanceOf<S2A_RULES>(res3);
			var typed_msg3 = (S2A_RULES)res3;
			Assert.AreEqual(typed_msg3.Rules.Count, 255);
			for (var i = 0; i < 255; i++)
			{
				Assert.AreEqual($"SuperLongAndBoringRuleValueThatHopefullyWillSplitMsgs_{i}", typed_msg3.Rules[i].Value);
				Assert.AreEqual($"SuperLongAndBoringRuleKeyThatHopefullyWillSplitMsgs_{i}", typed_msg3.Rules[i].Name);
			}

			Assert.AreEqual(2, c1SessionHdlr.GetActiveSessions().Count);
			Assert.AreEqual(1, c2SessionHdlr.GetActiveSessions().Count);
			Assert.AreEqual(2, s1SessionHdlr.GetActiveSessions().Count);
			Assert.AreEqual(1, s2SessionHdlr.GetActiveSessions().Count);

			// Checking that Challenges has been set
			Assert.NotNull(s1SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge());
			Assert.NotNull(s1SessionHdlr.GetSession(iPEndPoint_c2).GetChallenge());
			Assert.NotNull(s2SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge());
			Assert.NotNull(c1SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			Assert.NotNull(c1SessionHdlr.GetSession(iPEndPoint_s2).GetChallenge());
			Assert.NotNull(c2SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			Assert.AreEqual(s1SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge(), c1SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			Assert.AreEqual(s1SessionHdlr.GetSession(iPEndPoint_c2).GetChallenge(), c2SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			Assert.AreEqual(s2SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge(), c1SessionHdlr.GetSession(iPEndPoint_s2).GetChallenge());
			var savedChallengeExch3 = s2SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge();
			Assert.AreNotEqual(savedChallengeExch1, savedChallengeExch2);
			Assert.AreNotEqual(savedChallengeExch1, savedChallengeExch3);
			Assert.AreNotEqual(savedChallengeExch2, savedChallengeExch3);

			// Simulation 2nd client to 2nd server Exchange
			var res4 = ClientSessionHdlr2ServerSessionHdlrSimpleExchange(c2SessionHdlr, iPEndPoint_c2, s2SessionHdlr, iPEndPoint_s2, new A2S_RULES());
			Assert.IsInstanceOf<S2A_RULES>(res4);
			var typed_msg4 = (S2A_RULES)res4;
			Assert.AreEqual(typed_msg4.Rules.Count, 255);
			for (var i = 0; i < 255; i++)
			{
				Assert.AreEqual($"SuperLongAndBoringRuleValueThatHopefullyWillSplitMsgs_{i}", typed_msg4.Rules[i].Value);
				Assert.AreEqual($"SuperLongAndBoringRuleKeyThatHopefullyWillSplitMsgs_{i}", typed_msg4.Rules[i].Name);
			}

			Assert.AreEqual(2, c1SessionHdlr.GetActiveSessions().Count);
			Assert.AreEqual(2, c2SessionHdlr.GetActiveSessions().Count);
			Assert.AreEqual(2, s1SessionHdlr.GetActiveSessions().Count);
			Assert.AreEqual(2, s2SessionHdlr.GetActiveSessions().Count);

			// Checking that Challenges has been set
			Assert.NotNull(s1SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge());
			Assert.NotNull(s1SessionHdlr.GetSession(iPEndPoint_c2).GetChallenge());
			Assert.NotNull(s2SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge());
			Assert.NotNull(s2SessionHdlr.GetSession(iPEndPoint_c2).GetChallenge());
			Assert.NotNull(c1SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			Assert.NotNull(c1SessionHdlr.GetSession(iPEndPoint_s2).GetChallenge());
			Assert.NotNull(c2SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			Assert.NotNull(c2SessionHdlr.GetSession(iPEndPoint_s2).GetChallenge());
			Assert.AreEqual(s1SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge(), c1SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			Assert.AreEqual(s1SessionHdlr.GetSession(iPEndPoint_c2).GetChallenge(), c2SessionHdlr.GetSession(iPEndPoint_s1).GetChallenge());
			Assert.AreEqual(s2SessionHdlr.GetSession(iPEndPoint_c1).GetChallenge(), c1SessionHdlr.GetSession(iPEndPoint_s2).GetChallenge());
			Assert.AreEqual(s2SessionHdlr.GetSession(iPEndPoint_c2).GetChallenge(), c2SessionHdlr.GetSession(iPEndPoint_s2).GetChallenge());
			var savedChallengeExch4 = s2SessionHdlr.GetSession(iPEndPoint_c2).GetChallenge();
			Assert.AreNotEqual(savedChallengeExch1, savedChallengeExch2);
			Assert.AreNotEqual(savedChallengeExch1, savedChallengeExch3);
			Assert.AreNotEqual(savedChallengeExch1, savedChallengeExch4);
			Assert.AreNotEqual(savedChallengeExch2, savedChallengeExch3);
			Assert.AreNotEqual(savedChallengeExch2, savedChallengeExch4);
			Assert.AreNotEqual(savedChallengeExch3, savedChallengeExch4);
		}

		public static Message ClientSessionHdlr2ServerSessionHdlrSimpleExchange<TClientSession, TServerSession>(
			ClientSessionHandler<TClientSession> cSessionHdlr,
			IPEndPoint iPEndPoint_c,
			ServerSessionHandler<TServerSession> sSessionHdlr,
			IPEndPoint iPEndPoint_s, A2S_SimpleCommand cmd)
		where TServerSession : ServerSession, new()
		where TClientSession : ClientSession, new()
		{
			var c2s_frame = cSessionHdlr.SendCommand(iPEndPoint_s, cmd);
			var maxRound = 100;
			TClientSession cSessionCurrent;
			do
			{
				var s2c_frames = new List<Frame>();
				foreach (var pkt in c2s_frame)
				{
					var tmp_s2c_frame = sSessionHdlr.ConsumePacket(iPEndPoint_c, pkt);
					if (tmp_s2c_frame is not null)
						s2c_frames.Add(tmp_s2c_frame);
				}

				Assert.AreEqual(1, s2c_frames.Count);
				var tmp_c2s_frames = new List<Frame>();
				foreach (var pkt in s2c_frames[0])
				{
					var tmp_c2s_frame = cSessionHdlr.ConsumePacket(iPEndPoint_s, pkt);
					if (tmp_c2s_frame is not null)
						tmp_c2s_frames.Add(tmp_c2s_frame);
				}

				Assert.LessOrEqual(tmp_c2s_frames.Count, 1);
				if (tmp_c2s_frames.Count == 1)
					c2s_frame = tmp_c2s_frames[0];
				else
					c2s_frame = null;

				if (maxRound-- == 0) throw new Exception("Max rounds exceded");

				cSessionCurrent = cSessionHdlr.GetSession(iPEndPoint_s);
			}
			while (!cSessionCurrent.ResponseOk);

			return cSessionCurrent.Response;
		}

		[Test]
		public void ClientSession2ServerSessionHandler_S2A_RULES()
		{
			var sSessionHdlr = new TestServerSessionHandler();
			var cSession1 = new TestClientSession();
			var iPEndPoint1 = IPEndPoint.Parse("1.2.3.4:1234");

			Assert.AreEqual(0, sSessionHdlr.GetActiveSessions().Count);
			Assert.That(cSession1.GetChallenge(), Is.Null);

			// Simulation 1st client Exchange
			var res = ClientSession2ServerSessionHandlerSimpleExchange(cSession1, sSessionHdlr, iPEndPoint1, new A2S_RULES());
			Assert.IsInstanceOf<S2A_RULES>(res);
			var typed_msg = (S2A_RULES)res;
			Assert.AreEqual(typed_msg.Rules.Count, 255);
			for (var i = 0; i < 255; i++)
			{
				Assert.AreEqual($"SuperLongAndBoringRuleValueThatHopefullyWillSplitMsgs_{i}", typed_msg.Rules[i].Value);
				Assert.AreEqual($"SuperLongAndBoringRuleKeyThatHopefullyWillSplitMsgs_{i}", typed_msg.Rules[i].Name);
			}

			Assert.AreEqual(1, sSessionHdlr.GetActiveSessions().Count);

			// Checking that Challenges has been set
			Assert.NotNull(sSessionHdlr.GetActiveSessions()[iPEndPoint1].GetChallenge());
			Assert.NotNull(cSession1.GetChallenge());
			Assert.AreEqual(sSessionHdlr.GetActiveSessions()[iPEndPoint1].GetChallenge(), cSession1.GetChallenge());
			var savedChallengeC1 = cSession1.GetChallenge();

			// Simulation 2nd client Ewchange
			var cSession2 = new TestClientSession();
			var iPEndPoint2 = IPEndPoint.Parse("1.2.3.4:1235");

			var res2 = ClientSession2ServerSessionHandlerSimpleExchange(cSession2, sSessionHdlr, iPEndPoint2, new A2S_PLAYER());
			Assert.IsInstanceOf<S2A_PLAYER>(res2);
			var typed_msg2 = (S2A_PLAYER)res2;
			Assert.AreEqual(typed_msg2.Players.Count, 255);
			for (var i = 0; i < 255; i++)
			{
				Assert.AreEqual(i, typed_msg2.Players[i].Index);
				Assert.AreEqual($"SuperLongAndBoringPlayerNameThatHopefullyWillSplitMsgs_{i}", typed_msg2.Players[i].PlayerName);
				Assert.AreEqual(10 * i, typed_msg2.Players[i].Score);
				Assert.AreEqual(100f * i + 50f, typed_msg2.Players[i].Duration);
			}

			Assert.AreEqual(2, sSessionHdlr.GetActiveSessions().Count);

			// Checking that Challenges has been set
			Assert.NotNull(sSessionHdlr.GetActiveSessions()[iPEndPoint2].GetChallenge());
			Assert.NotNull(cSession2.GetChallenge());
			Assert.AreEqual(sSessionHdlr.GetActiveSessions()[iPEndPoint2].GetChallenge(), cSession2.GetChallenge());
			var savedChallengeC2 = cSession2.GetChallenge();
			Assert.AreNotEqual(savedChallengeC1, savedChallengeC2);
		}

		public static Message ClientSession2ServerSessionHandlerSimpleExchange<TServerSession>(TestClientSession cSession, ServerSessionHandler<TServerSession> sSessionHdlr, IPEndPoint iPEndPoint, A2S_SimpleCommand cmd) where TServerSession : ServerSession, new()
		{
			cSession.OnCommandCompletedCalled = false;
			var c2s_frame = cSession.SendCommand(cmd);
			var maxRound = 100;
			do
			{
				var s2c_frames = new List<Frame>();
				foreach (var pkt in c2s_frame)
				{
					var tmp_s2c_frame = sSessionHdlr.ConsumePacket(iPEndPoint, pkt);
					if (tmp_s2c_frame is not null)
						s2c_frames.Add(tmp_s2c_frame);
				}

				Assert.AreEqual(1, s2c_frames.Count);
				var tmp_c2s_frames = new List<Frame>();
				foreach (var pkt in s2c_frames[0])
				{
					var tmp_c2s_frame = cSession.ConsumePacket(pkt);
					if (tmp_c2s_frame is not null)
						tmp_c2s_frames.Add(tmp_c2s_frame);
				}

				Assert.LessOrEqual(tmp_c2s_frames.Count, 1);
				if (tmp_c2s_frames.Count == 1)
					c2s_frame = tmp_c2s_frames[0];
				else
					c2s_frame = null;

				if (maxRound-- == 0) throw new Exception("Max rounds exceded");
			}
			while (!cSession.ResponseOk);

			Assert.IsTrue(cSession.OnCommandCompletedCalled);

			return cSession.Response;
		}
	}
	#endregion

	#region Session
	sealed class QueryStatsTest_Session
	{
		[SetUp]
		public void TestSetUp()
		{
			MessageFactory.GetFactory().CustomMessages.Clear();
		}

		[TearDown]
		public void TestTearDown()
		{
			MessageFactory.GetFactory().CustomMessages.Clear();
		}

		[Test]
		public void ClientServerSession_A2S_INFO_Payload()
		{
			var sSession = new TestServerSession_Payload() { CheckA2SINFOPayload = false };
			var cSession = new TestClientSession();

			Assert.Throws<TestException>(() => ClientServerSessionSimpleFrameExchange(sSession, cSession, new A2S_INFO()));
			cSession.ResetCommand();

			Assert.Throws<TestException>(() => ClientServerSessionSimpleFrameExchange(sSession, cSession, new A2S_INFO() { Payload = "WRONG VALUE" }));
			cSession.ResetCommand();

			var res = ClientServerSessionSimpleFrameExchange(sSession, cSession, new A2S_INFO() { Payload = "TEST CHECK" });
			Assert.IsInstanceOf<S2A_INFO>(res);
		}

		[Test]
		public void ClientServerSession_A2S_PLAYERS()
		{
			var res = ClientServerSessionSimpleFrameExchange(new TestServerSession(), new TestClientSession(), new A2S_PLAYER());
			Assert.IsInstanceOf<S2A_PLAYER>(res);
			var typed_msg = (S2A_PLAYER)res;
			Assert.AreEqual(typed_msg.Players.Count, 255);
			for (var i = 0; i < 255; i++)
			{
				Assert.AreEqual(i, typed_msg.Players[i].Index);
				Assert.AreEqual($"SuperLongAndBoringPlayerNameThatHopefullyWillSplitMsgs_{i}", typed_msg.Players[i].PlayerName);
				Assert.AreEqual(10 * i, typed_msg.Players[i].Score);
				Assert.AreEqual(100f * i + 50f, typed_msg.Players[i].Duration);
			}
		}

		[Test]
		public void ClientServerSession_A2S_RULES()
		{
			var res = ClientServerSessionSimpleFrameExchange(new TestServerSession(), new TestClientSession(), new A2S_RULES());
			Assert.IsInstanceOf<S2A_RULES>(res);
			var typed_msg = (S2A_RULES)res;
			Assert.AreEqual(typed_msg.Rules.Count, 255);
			for (var i = 0; i < 255; i++)
			{
				Assert.AreEqual($"SuperLongAndBoringRuleValueThatHopefullyWillSplitMsgs_{i}", typed_msg.Rules[i].Value);
				Assert.AreEqual($"SuperLongAndBoringRuleKeyThatHopefullyWillSplitMsgs_{i}", typed_msg.Rules[i].Name);
			}
		}

		[Test]
		public void ClientServerSession_WrongID()
		{
			var sSession = new TestServerSession();
			var cSession = new TestClientSession();

			Assert.Throws<ExceptionFrameWrongPacketID>(() => ClientServerSessionSimpleFrameExchange(sSession, cSession, new A2S_RULES(), true));
			Assert.Throws<PreviousCommandNotCompleted>(() => ClientServerSessionSimpleFrameExchange(sSession, cSession, new A2S_RULES(), false));
			cSession.ResetCommand();
			ClientServerSessionSimpleFrameExchange(sSession, cSession, new A2S_RULES(), false);
		}

		[Test]
		public void ClientServerSession_A2S_INFO()
		{
			var serverSession = new TestServerSession();
			var clientSession = new TestClientSession();

			// Insuring initial Challenges are not set
			Assert.That(serverSession.GetChallenge(), Is.Null);
			Assert.That(clientSession.GetChallenge(), Is.Null);

			// Processing a regular exchange
			var res = ClientServerSessionSimpleFrameExchange(serverSession, clientSession, new A2S_INFO());
			Assert.IsInstanceOf<S2A_INFO>(res);
			var typed_msg = (S2A_INFO)res;
			Assert.That(typed_msg.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg.Protocol, Is.EqualTo(48));
			Assert.That(typed_msg.Name, Is.EqualTo("NAME65"));
			Assert.That(typed_msg.Map, Is.EqualTo("MAP14"));
			Assert.That(typed_msg.Folder, Is.EqualTo("FOLDER78"));
			Assert.That(typed_msg.Game, Is.EqualTo("GAME14"));
			Assert.That(typed_msg.ID, Is.EqualTo(0x1564));
			Assert.That(typed_msg.Players, Is.EqualTo(7));
			Assert.That(typed_msg.MaxPlayers, Is.EqualTo(8));
			Assert.That(typed_msg.Bots, Is.EqualTo(96));
			Assert.That(typed_msg.ServerType, Is.EqualTo((byte)'l'));
			Assert.That(typed_msg.Environment, Is.EqualTo((byte)'m'));
			Assert.That(typed_msg.Visibility, Is.EqualTo(98));
			Assert.That(typed_msg.VAC, Is.EqualTo(65));
			Assert.That(typed_msg.Version, Is.EqualTo("2.3.1"));
			Assert.That(typed_msg.ExtraDataFlag, Is.EqualTo(0xF1));
			Assert.That(typed_msg.Port, Is.EqualTo(0x3a5b));
			Assert.That(typed_msg.SteamID, Is.EqualTo(0x129875a6b5e5f912));
			Assert.That(typed_msg.SpecPort, Is.EqualTo(0x5298));
			Assert.That(typed_msg.SpecName, Is.EqualTo("abcd"));
			Assert.That(typed_msg.Keywords, Is.EqualTo("efgh"));
			Assert.That(typed_msg.GameID, Is.EqualTo(0x095a3b5c3f69e563));

			// Checking that Challenges has been set
			Assert.NotNull(serverSession.GetChallenge());
			Assert.NotNull(clientSession.GetChallenge());
			Assert.AreEqual(serverSession.GetChallenge(), clientSession.GetChallenge());
			var savedChallenge = serverSession.GetChallenge();

			// Processing the same exchange again
			var res2 = ClientServerSessionSimpleFrameExchange(serverSession, clientSession, new A2S_INFO());
			Assert.IsInstanceOf<S2A_INFO>(res2);
			var typed_msg2 = (S2A_INFO)res2;
			Assert.That(typed_msg2.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg2.Version, Is.EqualTo("2.3.1"));

			// Checking that the challenge has been reused
			Assert.NotNull(serverSession.GetChallenge());
			Assert.NotNull(clientSession.GetChallenge());
			Assert.AreEqual(serverSession.GetChallenge(), clientSession.GetChallenge());
			Assert.AreEqual(savedChallenge, serverSession.GetChallenge());

			// Reseting the Client Session (reset challenge)
			clientSession = new TestClientSession();

			// Processing the same exchange again
			var res3 = ClientServerSessionSimpleFrameExchange(serverSession, clientSession, new A2S_INFO());
			Assert.IsInstanceOf<S2A_INFO>(res3);
			var typed_msg3 = (S2A_INFO)res3;
			Assert.That(typed_msg3.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg3.Version, Is.EqualTo("2.3.1"));

			// Checking that a new Challenge has been generated
			Assert.NotNull(serverSession.GetChallenge());
			Assert.NotNull(clientSession.GetChallenge());
			Assert.AreEqual(serverSession.GetChallenge(), clientSession.GetChallenge());
			Assert.AreNotEqual(savedChallenge, serverSession.GetChallenge());
			savedChallenge = serverSession.GetChallenge();

			// Processing the same exchange again
			var res4 = ClientServerSessionSimpleFrameExchange(serverSession, clientSession, new A2S_INFO());
			Assert.IsInstanceOf<S2A_INFO>(res4);
			var typed_msg4 = (S2A_INFO)res4;
			Assert.That(typed_msg4.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg4.Version, Is.EqualTo("2.3.1"));

			// Checking that the challenge has been reused
			Assert.NotNull(serverSession.GetChallenge());
			Assert.NotNull(clientSession.GetChallenge());
			Assert.AreEqual(serverSession.GetChallenge(), clientSession.GetChallenge());
			Assert.AreEqual(savedChallenge, serverSession.GetChallenge());

			// Faking UpdateTime
			serverSession.FakeLastUpdatedDateTime(DateTime.Now - serverSession.SessionChallengeTTLUpdate - TimeSpan.FromSeconds(1));
			var res5 = ClientServerSessionSimpleFrameExchange(serverSession, clientSession, new A2S_INFO());
			Assert.IsInstanceOf<S2A_INFO>(res5);
			var typed_msg5 = (S2A_INFO)res5;
			Assert.That(typed_msg5.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg5.Version, Is.EqualTo("2.3.1"));

			// Checking that a new Challenge has been generated
			Assert.NotNull(serverSession.GetChallenge());
			Assert.NotNull(clientSession.GetChallenge());
			Assert.AreEqual(serverSession.GetChallenge(), clientSession.GetChallenge());
			Assert.AreNotEqual(savedChallenge, serverSession.GetChallenge());
			savedChallenge = serverSession.GetChallenge();

			// Processing the same exchange again
			var res6 = ClientServerSessionSimpleFrameExchange(serverSession, clientSession, new A2S_INFO());
			Assert.IsInstanceOf<S2A_INFO>(res6);
			var typed_msg6 = (S2A_INFO)res6;
			Assert.That(typed_msg6.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg6.Version, Is.EqualTo("2.3.1"));

			// Checking that the challenge has been reused
			Assert.NotNull(serverSession.GetChallenge());
			Assert.NotNull(clientSession.GetChallenge());
			Assert.AreEqual(serverSession.GetChallenge(), clientSession.GetChallenge());
			Assert.AreEqual(savedChallenge, serverSession.GetChallenge());

			// Faking CreationDateTime
			serverSession.FakeChallengeCreationDateTime(DateTime.Now - serverSession.SessionChallengeTTLCreation - TimeSpan.FromSeconds(1));
			var res7 = ClientServerSessionSimpleFrameExchange(serverSession, clientSession, new A2S_INFO());
			Assert.IsInstanceOf<S2A_INFO>(res7);
			var typed_msg7 = (S2A_INFO)res7;
			Assert.That(typed_msg7.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg7.Version, Is.EqualTo("2.3.1"));

			// Checking that a new Challenge has been generated
			Assert.NotNull(serverSession.GetChallenge());
			Assert.NotNull(clientSession.GetChallenge());
			Assert.AreEqual(serverSession.GetChallenge(), clientSession.GetChallenge());
			Assert.AreNotEqual(savedChallenge, serverSession.GetChallenge());
			savedChallenge = serverSession.GetChallenge();

			// Processing the same exchange again
			var res8 = ClientServerSessionSimpleFrameExchange(serverSession, clientSession, new A2S_INFO());
			Assert.IsInstanceOf<S2A_INFO>(res8);
			var typed_msg8 = (S2A_INFO)res8;
			Assert.That(typed_msg8.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg8.Version, Is.EqualTo("2.3.1"));

			// Checking that the challenge has been reused
			Assert.NotNull(serverSession.GetChallenge());
			Assert.NotNull(clientSession.GetChallenge());
			Assert.AreEqual(serverSession.GetChallenge(), clientSession.GetChallenge());
			Assert.AreEqual(savedChallenge, serverSession.GetChallenge());
		}

		public static Message ClientServerSessionSimpleFrameExchange(ServerSession sSession, ClientSession cSession, A2S_SimpleCommand cmd, bool injectIDError = false)
		{
			var c2s_frame = cSession.SendCommand(cmd);
			var maxRound = 100;
			do
			{
				var s2c_frames = ServerSessionSimpleFrameExchange(sSession, c2s_frame);
				Assert.AreEqual(1, s2c_frames.Count);
				var tmp_c2s_frames = new List<Frame>();
				var index = 0;
				foreach (var pkt in s2c_frames[0])
				{
					if (index++ == 1 && injectIDError && pkt is PacketLong packetLong)
					{
						packetLong.ID--;
					}

					var tmp_c2s_frame = cSession.ConsumePacket(pkt);
					if (tmp_c2s_frame is not null)
						tmp_c2s_frames.Add(tmp_c2s_frame);
				}

				Assert.LessOrEqual(tmp_c2s_frames.Count, 1);
				if (tmp_c2s_frames.Count == 1)
					c2s_frame = tmp_c2s_frames[0];
				else
					c2s_frame = null;

				if (maxRound-- == 0) throw new Exception("Max rounds exceded");
			}
			while (!cSession.ResponseOk);

			return cSession.Response;
		}

		[Test]
		public void ServerSession_S2A_INFO()
		{
			var session = new TestServerSession();

			// 1st Frame : no Challenge
			var res = ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO()));
			Assert.AreEqual(1, res.Count);
			var resp_frame = res[0];
			Assert.IsInstanceOf<S2C_CHALLENGE>(resp_frame.Msg);
			var typed_msg = (S2C_CHALLENGE)resp_frame.Msg;

			// 2nd Frame : with good Challenge
			var res2 = ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO() { Challenge = typed_msg.Challenge }));
			Assert.AreEqual(1, res2.Count);
			var resp_frame2 = res2[0];
			Assert.IsInstanceOf<S2A_INFO>(resp_frame2.Msg);
			var typed_msg2 = (S2A_INFO)resp_frame2.Msg;
			Assert.That(typed_msg2.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg2.Protocol, Is.EqualTo(48));
			Assert.That(typed_msg2.Name, Is.EqualTo("NAME65"));
			Assert.That(typed_msg2.Map, Is.EqualTo("MAP14"));
			Assert.That(typed_msg2.Folder, Is.EqualTo("FOLDER78"));
			Assert.That(typed_msg2.Game, Is.EqualTo("GAME14"));
			Assert.That(typed_msg2.ID, Is.EqualTo(0x1564));
			Assert.That(typed_msg2.Players, Is.EqualTo(7));
			Assert.That(typed_msg2.MaxPlayers, Is.EqualTo(8));
			Assert.That(typed_msg2.Bots, Is.EqualTo(96));
			Assert.That(typed_msg2.ServerType, Is.EqualTo((byte)'l'));
			Assert.That(typed_msg2.Environment, Is.EqualTo((byte)'m'));
			Assert.That(typed_msg2.Visibility, Is.EqualTo(98));
			Assert.That(typed_msg2.VAC, Is.EqualTo(65));
			Assert.That(typed_msg2.Version, Is.EqualTo("2.3.1"));
			Assert.That(typed_msg2.ExtraDataFlag, Is.EqualTo(0xF1));
			Assert.That(typed_msg2.Port, Is.EqualTo(0x3a5b));
			Assert.That(typed_msg2.SteamID, Is.EqualTo(0x129875a6b5e5f912));
			Assert.That(typed_msg2.SpecPort, Is.EqualTo(0x5298));
			Assert.That(typed_msg2.SpecName, Is.EqualTo("abcd"));
			Assert.That(typed_msg2.Keywords, Is.EqualTo("efgh"));
			Assert.That(typed_msg2.GameID, Is.EqualTo(0x095a3b5c3f69e563));

			// 3rd Frame : with bad Challenge
			Assert.Throws<WrongChallenge>(() => ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO() { Challenge = typed_msg.Challenge + 1 })));

			// 4th Frame : with good Challenge
			var res4 = ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO() { Challenge = typed_msg.Challenge }));
			Assert.AreEqual(1, res4.Count);
			var resp_frame4 = res4[0];
			Assert.IsInstanceOf<S2A_INFO>(resp_frame4.Msg);
			var typed_msg4 = (S2A_INFO)resp_frame4.Msg;
			Assert.That(typed_msg4.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg4.Protocol, Is.EqualTo(48));
			Assert.That(typed_msg4.Version, Is.EqualTo("2.3.1"));
			/* Not retesting all the values */

			// 5th Frame : reset Challenge
			var res5 = ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO() { Challenge = -1 }));
			Assert.AreEqual(1, res5.Count);
			var resp_frame5 = res5[0];
			Assert.IsInstanceOf<S2C_CHALLENGE>(resp_frame5.Msg);
			var typed_msg5 = (S2C_CHALLENGE)resp_frame5.Msg;
			Assert.AreNotEqual(typed_msg.Challenge, typed_msg5.Challenge);

			// 6th Frame : with new Challenge
			var res6 = ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO() { Challenge = typed_msg5.Challenge }));
			Assert.AreEqual(1, res6.Count);
			var resp_frame6 = res6[0];
			Assert.IsInstanceOf<S2A_INFO>(resp_frame6.Msg);
			var typed_msg6 = (S2A_INFO)resp_frame6.Msg;
			Assert.That(typed_msg6.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg6.Version, Is.EqualTo("2.3.1"));
			/* Not retesting all the values */

			// 7th Frame : with old Challenge
			Assert.Throws<WrongChallenge>(() => ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO() { Challenge = typed_msg.Challenge })));

			// 8th Frame : with good Challenge
			var res8 = ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO() { Challenge = typed_msg5.Challenge }));
			Assert.AreEqual(1, res8.Count);
			var resp_frame8 = res8[0];
			Assert.IsInstanceOf<S2A_INFO>(resp_frame8.Msg);
			var typed_msg8 = (S2A_INFO)resp_frame8.Msg;
			Assert.That(typed_msg8.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg8.Version, Is.EqualTo("2.3.1"));
			/* Not retesting all the values */

			// 9th Frame: Faking update time => new challenge requested, then work
			session.FakeLastUpdatedDateTime(DateTime.Now - session.SessionChallengeTTLUpdate - TimeSpan.FromSeconds(1));
			var res9 = ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO() { Challenge = typed_msg5.Challenge }));
			Assert.AreEqual(1, res9.Count);
			var resp_frame9 = res9[0];
			Assert.IsInstanceOf<S2C_CHALLENGE>(resp_frame9.Msg);
			var typed_msg9 = (S2C_CHALLENGE)resp_frame9.Msg;
			Assert.AreNotEqual(typed_msg5.Challenge, typed_msg9.Challenge);
			res9 = ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO() { Challenge = typed_msg9.Challenge }));
			Assert.AreEqual(1, res9.Count);
			resp_frame9 = res9[0];
			Assert.IsInstanceOf<S2A_INFO>(resp_frame9.Msg);
			var typed_msg9_2 = (S2A_INFO)resp_frame9.Msg;
			Assert.That(typed_msg9_2.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg9_2.Version, Is.EqualTo("2.3.1"));
			/* Not retesting all the values */

			// 10th Frame : with good Challenge
			var res10 = ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO() { Challenge = typed_msg9.Challenge }));
			Assert.AreEqual(1, res10.Count);
			var resp_frame10 = res10[0];
			Assert.IsInstanceOf<S2A_INFO>(resp_frame10.Msg);
			var typed_msg10 = (S2A_INFO)resp_frame10.Msg;
			Assert.That(typed_msg10.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg10.Version, Is.EqualTo("2.3.1"));
			/* Not retesting all the values */

			// 9th Frame: Faking Creation time => new challenge requested, then work
			session.FakeChallengeCreationDateTime(DateTime.Now - session.SessionChallengeTTLCreation - TimeSpan.FromSeconds(1));
			var res11 = ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO() { Challenge = typed_msg5.Challenge }));
			Assert.AreEqual(1, res11.Count);
			var resp_frame11 = res11[0];
			Assert.IsInstanceOf<S2C_CHALLENGE>(resp_frame11.Msg);
			var typed_msg11 = (S2C_CHALLENGE)resp_frame11.Msg;
			Assert.AreNotEqual(typed_msg5.Challenge, typed_msg11.Challenge);
			res11 = ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO() { Challenge = typed_msg11.Challenge }));
			Assert.AreEqual(1, res11.Count);
			resp_frame11 = res11[0];
			Assert.IsInstanceOf<S2A_INFO>(resp_frame11.Msg);
			var typed_msg11_2 = (S2A_INFO)resp_frame11.Msg;
			Assert.That(typed_msg11_2.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg11_2.Version, Is.EqualTo("2.3.1"));
			/* Not retesting all the values */

			// 8th Frame : with good Challenge
			var res12 = ServerSessionSimpleFrameExchange(session, new Frame(new A2S_INFO() { Challenge = typed_msg11.Challenge }));
			Assert.AreEqual(1, res12.Count);
			var resp_frame12 = res12[0];
			Assert.IsInstanceOf<S2A_INFO>(resp_frame12.Msg);
			var typed_msg12 = (S2A_INFO)resp_frame12.Msg;
			Assert.That(typed_msg12.Header, Is.EqualTo(0x49));
			Assert.That(typed_msg12.Version, Is.EqualTo("2.3.1"));
			/* Not retesting all the values */
		}

		static IList<Frame> ServerSessionSimpleFrameExchange(ServerSession ssession, Frame in_frame)
		{
			var resp_frames = new List<Frame>();
			foreach (var pkt in in_frame)
			{
				var frame = ssession.ConsumePacket(pkt);
				if (frame != null)
					resp_frames.Add(frame);
			}

			return resp_frames;
		}
	}
	#endregion

	#region Transport
	[TestFixture]
	sealed class QueryStatsTest_Transport
	{
		[SetUp]
		public void TestSetUp()
		{
			MessageFactory.GetFactory().CustomMessages.Clear();
		}

		[TearDown]
		public void TestTearDown()
		{
			MessageFactory.GetFactory().CustomMessages.Clear();
		}

		[Test]
		public void Frame_OutOfOrder()
		{
			var msg = new S2A_RULES();
			for (var i = 0; i < 255; i++)
				msg.Rules.Add(new S2A_RULE_DATA { Name = $"SuperLongAndBoringRuleKeyThatHopefullyWillSplitMsgs_{i}", Value = $"SuperLongAndBoringRuleValueThatHopefullyWillSplitMsgs_{i}" });
			var nbpkt = Frame_test(msg, true);
			Assert.Greater(nbpkt, 1);

			var msg6 = new S2A_PLAYER();
			for (var i = 0; i < 255; i++)
				msg6.Players.Add(new S2A_PLAYER_DATA { Index = (byte)i, PlayerName = $"SuperLongAndBoringPlayerNameThatHopefullyWillSplitMsgs_{i}", Score = 10 * i, Duration = 100f * i + 50f });
			nbpkt = Frame_test(msg6, true);
			Assert.Greater(nbpkt, 1);
		}

		[Test]
		public void Frame_IDError()
		{
			var msg = new S2A_RULES();
			for (var i = 0; i < 255; i++)
				msg.Rules.Add(new S2A_RULE_DATA { Name = $"SuperLongAndBoringRuleKeyThatHopefullyWillSplitMsgs_{i}", Value = $"SuperLongAndBoringRuleValueThatHopefullyWillSplitMsgs_{i}" });
			Assert.Throws<ExceptionFrameWrongPacketID>(() => Frame_test(msg, false, true));
		}

		[Test]
		public void Frame_Nominal()
		{
			// traversing full stack from message to Frame to pkts and going back, then comparing using serialization/deserialization.
			var msg = new S2A_RULES();
			for (var i = 0; i < 255; i++)
				msg.Rules.Add(new S2A_RULE_DATA { Name = $"SuperLongAndBoringRuleKeyThatHopefullyWillSplitMsgs_{i}", Value = $"SuperLongAndBoringRuleValueThatHopefullyWillSplitMsgs_{i}" });
			var nbpkt = Frame_test(msg);
			Assert.Greater(nbpkt, 1);

			var msg2 = new A2S_PLAYER();
			nbpkt = Frame_test(msg2);
			Assert.AreEqual(nbpkt, 1);
			msg2.Challenge = 0x12435632;
			nbpkt = Frame_test(msg2);
			Assert.AreEqual(nbpkt, 1);

			var msg3 = new A2S_RULES();
			nbpkt = Frame_test(msg3);
			Assert.AreEqual(nbpkt, 1);
			msg3.Challenge = 0x12435632;
			nbpkt = Frame_test(msg3);
			Assert.AreEqual(nbpkt, 1);

			var msg4 = new A2S_INFO()
			{
				Payload = "MySuperTest"
			};
			nbpkt = Frame_test(msg4);
			Assert.AreEqual(nbpkt, 1);
			msg4.Challenge = 0x12435632;
			nbpkt = Frame_test(msg4);
			Assert.AreEqual(nbpkt, 1);

			var msg5 = new S2A_INFO
			{
				Name = "NAME65",
				Map = "MAP14",
				Folder = "FOLDER78",
				Game = "GAME14",
				ID = 0x1564,
				Players = 7,
				MaxPlayers = 8,
				Bots = 96,
				ServerType = (byte)'l',
				Environment = (byte)'m',
				Visibility = 98,
				VAC = 65,
				Version = "2.3.1",
				Port = 0x3a5b,
				SteamID = 0x129875a6b5e5f912,
				SpecPort = 0x5298,
				SpecName = "abcd",
				Keywords = "efgh",
				GameID = 0x095a3b5c3f69e563
			};
			nbpkt = Frame_test(msg5);
			Assert.AreEqual(nbpkt, 1);

			var msg6 = new S2A_PLAYER();
			for (var i = 0; i < 255; i++)
				msg6.Players.Add(new S2A_PLAYER_DATA { Index = (byte)i, PlayerName = $"SuperLongAndBoringPlayerNameThatHopefullyWillSplitMsgs_{i}", Score = 10 * i, Duration = 100f * i + 50f });
			nbpkt = Frame_test(msg6);
			Assert.Greater(nbpkt, 1);

			var msg7 = new S2C_CHALLENGE
			{
				Challenge = 0x72587169
			};
			nbpkt = Frame_test(msg7);
			Assert.AreEqual(nbpkt, 1);
		}

		public static int Frame_test<T>(T msg, bool reversePkt = false, bool injectIDError = false) where T : Message
		{
			var frameIn = new Frame(msg);
			Frame frameOut = null;

			IList<Packet> pktInList = frameIn.ToList();

			if (reversePkt)
				pktInList = pktInList.Reverse().ToList();

			if (injectIDError && pktInList.Count > 1 && pktInList[1] is PacketLong packetLong)
			{
				packetLong.ID--;
			}

			Assert.Greater(pktInList.Count, 0);
			if (pktInList.Count == 1)
			{
				Assert.IsInstanceOf<PacketShort>(pktInList[0]);
				frameOut = new Frame(pktInList[0]);
			}
			else
			{
				for (var index = 0; index < pktInList.Count; index++)
				{
					var pkt = pktInList[index];
					Assert.IsInstanceOf<PacketLong>(pkt);
					if (frameOut is null)
					{
						frameOut = new Frame(pkt);
					}
					else
					{
						frameOut.AddPacket((PacketLong)pkt);
					}
				}
			}

			Assert.NotNull(frameOut);
			Assert.IsTrue(frameOut.Completed);
			Assert.AreEqual(msg.Serialize().ToArray(), frameOut.Msg.Serialize().ToArray());
			return pktInList.Count;
		}
	}
	#endregion

	#region Protocol
	[TestFixture]
	sealed class QueryStatsTest_Protocol
	{
		[SetUp]
		public void TestSetUp()
		{
			MessageFactory.GetFactory().CustomMessages.Clear();
		}

		[TearDown]
		public void TestTearDown()
		{
			MessageFactory.GetFactory().CustomMessages.Clear();
		}

		[Test]
		public void Fatory()
		{
			// Get factory and test Singleton
			var factory = PacketFactory.GetFactory();
			var factory2 = PacketFactory.GetFactory();
			Assert.AreSame(factory, factory2);

			// Short Message (without data)
			var msg = factory.GetMessage(new MemoryStream(new byte[]
			{
				0xFF, 0xFF, 0xFF, 0xFF, // Header (-1)
			}));
			Assert.IsInstanceOf<PacketShort>(msg);
			var msg_typed = (PacketShort)msg;
			Assert.AreEqual(msg_typed.Payload.Length, 0);
			var test_out = msg.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0xFF, 0xFF, 0xFF, 0xFF, // Header (-1)
			}));

			// Short Message (with data)
			msg = factory.GetMessage(new MemoryStream(new byte[]
			{
				0xFF, 0xFF, 0xFF, 0xFF, // Header (-1)
				0x01, 0x23, 0x45, 0x67 // Payload
			}));
			Assert.IsInstanceOf<PacketShort>(msg);
			msg_typed = (PacketShort)msg;
			Assert.AreEqual(msg_typed.Payload.Length, 4);
			Assert.AreEqual(msg_typed.Payload.ToArray(), new byte[]
			{
				0x01, 0x23, 0x45, 0x67 // Payload
			});
			test_out = msg.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0xFF, 0xFF, 0xFF, 0xFF, // Header (-1)
				0x01, 0x23, 0x45, 0x67 // Payload
			}));

			// Long Message (without data)
			msg = factory.GetMessage(new MemoryStream(new byte[]
			{
				0xFE, 0xFF, 0xFF, 0xFF, // Header (-2)
				0x07, 0x00, 0x00, 0x00, // ID
				0x01, // Total
				0x00, // Number
				0xE0, 0x04, // Size
			}));
			Assert.IsInstanceOf<PacketLong>(msg);
			var msg_typed2 = (PacketLong)msg;
			Assert.AreEqual(msg_typed2.ID, 7);
			Assert.AreEqual(msg_typed2.Total, 1);
			Assert.AreEqual(msg_typed2.Number, 0);
			Assert.AreEqual(msg_typed2.Size, 1248);
			Assert.AreEqual(msg_typed2.Payload.Length, 0);
			test_out = msg.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0xFE, 0xFF, 0xFF, 0xFF, // Header (-2)
				0x07, 0x00, 0x00, 0x00, // ID
				0x01, // Total
				0x00, // Number
				0xE0, 0x04, // Size
			}));

			// Long Message (with data)
			msg = factory.GetMessage(new MemoryStream(new byte[]
			{
				0xFE, 0xFF, 0xFF, 0xFF, // Header (-2)
				0x07, 0x00, 0x00, 0x00, // ID
				0x01, // Total
				0x00, // Number
				0xE0, 0x04, // Size
				0x01, 0x23, 0x45, 0x67, 0x01, 0x23, 0x45, 0x67// Payload
			}));
			Assert.IsInstanceOf<PacketLong>(msg);
			msg_typed2 = (PacketLong)msg;
			Assert.AreEqual(msg_typed2.ID, 7);
			Assert.AreEqual(msg_typed2.Total, 1);
			Assert.AreEqual(msg_typed2.Number, 0);
			Assert.AreEqual(msg_typed2.Size, 1248);
			Assert.AreEqual(msg_typed2.Payload.Length, 8);
			Assert.AreEqual(msg_typed2.Payload.ToArray(), new byte[]
			{
				0x01, 0x23, 0x45, 0x67, 0x01, 0x23, 0x45, 0x67// Payload
			});
			test_out = msg.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0xFE, 0xFF, 0xFF, 0xFF, // Header (-2)
				0x07, 0x00, 0x00, 0x00, // ID
				0x01, // Total
				0x00, // Number
				0xE0, 0x04, // Size
				0x01, 0x23, 0x45, 0x67, 0x01, 0x23, 0x45, 0x67// Payload
			}));
		}

		[Test]
		public void PacketLong_CompressedFirst()
		{
			// Serialize (Default Values)
			var instance1 = new PacketLong
			{
				ID_compressed = true,
				CompressedSize = 0,
				CompressedCRC32 = 0
			};
			var test_out = instance1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0xFE, 0xFF, 0xFF, 0xFF, // Header (-2)
				0x00, 0x00, 0x00, 0x80, // ID (with compressed bit enabled)
				0x01, // Total
				0x00, // Number
				0xE0, 0x04, // Size
				0x00, 0x00, 0x00, 0x00, // CompressedSize
				0x00, 0x00, 0x00, 0x00, // CompressedCRC32
			}));
			instance1.CompressedSize = 0x1234;
			instance1.CompressedCRC32 = 0x7654;

			// Serialize (Custom Values)
			test_out = instance1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0xFE, 0xFF, 0xFF, 0xFF, // Header (-2)
				0x00, 0x00, 0x00, 0x80, // ID (with compressed bit enabled)
				0x01, // Total
				0x00, // Number
				0xE0, 0x04, // Size
				0x34, 0x12, 0x00, 0x00, // CompressedSize
				0x54, 0x76, 0x00, 0x00, // CompressedCRC32
			}));

			// UnSerialize (Simple)
			var test_in = new MemoryStream(new byte[]
			{
				0xFE, 0xFF, 0xFF, 0xFF, // Header (-2)
				0x00, 0x00, 0x00, 0x80, // ID (with compressed bit enabled)
				7, // Total
				0, // Number
				0xE0, 0x04, // Size
				0x34, 0x12, 0xAB, 0xCD, // CompressedSize
				0x54, 0x76, 0xEF, 0x90, // CompressedCRC32
			});
			instance1 = new PacketLong();
			instance1.UnSerialize(test_in);
			Assert.That(instance1.Header, Is.EqualTo(-2));
			Assert.That((uint)instance1.ID, Is.EqualTo(0x80000000));
			Assert.That(instance1.Total, Is.EqualTo(7));
			Assert.That(instance1.Number, Is.EqualTo(0));
			Assert.That(instance1.Size, Is.EqualTo(0x04E0));
			Assert.That(instance1.Payload.Length, Is.EqualTo(0));
			Assert.That((uint)instance1.CompressedSize, Is.EqualTo(0xCDAB1234));
			Assert.That((uint)instance1.CompressedCRC32, Is.EqualTo(0x90EF7654));

			// Chaining Serialize / UnSerialize
			var test_msg2 = new PacketLong
			{
				ID_compressed = true,
				ID = 0x1234,
				Total = 2,
				Number = 0,
				CompressedSize = 0x0DAB1234,
				CompressedCRC32 = 0x00EF7654,
				Payload = new MemoryStream(new byte[]
				{
					0x58, 0xAB, 0x45, 0xfe
				})
			};
			var test_msg3 = new PacketLong();
			test_msg3.UnSerialize(test_msg2.Serialize());
			Assert.That(test_msg3.Header, Is.EqualTo(-2));
			Assert.That((uint)test_msg3.ID, Is.EqualTo(0x80001234));
			Assert.That(test_msg3.Total, Is.EqualTo(2));
			Assert.That(test_msg3.Number, Is.EqualTo(0));
			Assert.That(test_msg3.Size, Is.EqualTo(0x04E0));
			Assert.That((uint)test_msg3.CompressedSize, Is.EqualTo(0x0DAB1234));
			Assert.That((uint)test_msg3.CompressedCRC32, Is.EqualTo(0x00EF7654));
			Assert.That(test_msg3.Payload.ToArray(), Is.EqualTo(new byte[]
			{
				0x58, 0xAB, 0x45, 0xfe // Payload
			}));
		}

		[Test]
		public void PacketLong()
		{
			var instance1 = new PacketLong();

			// Serialize (Default Values)
			var test_out = instance1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0xFE, 0xFF, 0xFF, 0xFF, // Header (-2)
				0x00, 0x00, 0x00, 0x00, // ID
				0x01, // Total
				0x00, // Number
				0xE0, 0x04, // Size
			}));

			// Serialize (With Payload and values)
			instance1.ID = 0x06897412;
			instance1.Total = 12;
			instance1.Number = 8;
			instance1.Payload = new MemoryStream(new byte[]
			{
				0x01, 0x23, 0x45, 0x67
			});
			test_out = instance1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0xFE, 0xFF, 0xFF, 0xFF, // Header (-2)
				0x12, 0x74, 0x89, 0x06, // ID
				12, // Total
				8, // Number
				0xE0, 0x04, // Size
				0x01, 0x23, 0x45, 0x67 // Payload
			}));

			// UnSerialize (Simple)
			var test_in = new MemoryStream(new byte[]
			{
				0xFE, 0xFF, 0xFF, 0xFF, // Header (-2)
				0x12, 0x00, 0x34, 0x00, // ID
				7, // Total
				3, // Number
				0xE0, 0x04, // Size
			});
			instance1 = new PacketLong();
			instance1.UnSerialize(test_in);
			Assert.That(instance1.Header, Is.EqualTo(-2));
			Assert.That(instance1.ID, Is.EqualTo(0x00340012));
			Assert.That(instance1.Total, Is.EqualTo(7));
			Assert.That(instance1.Number, Is.EqualTo(3));
			Assert.That(instance1.Size, Is.EqualTo(0x04E0));
			Assert.That(instance1.Payload.Length, Is.EqualTo(0));

			// UnSerialize (With Payload)
			test_in = new MemoryStream(new byte[]
			{
				0xFE, 0xFF, 0xFF, 0xFF, // Header (-2)
				0x13, 0x00, 0x54, 0x00, // ID
				9, // Total
				1, // Number
				0xE0, 0x04, // Size
				0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF // Payload
			});
			instance1 = new PacketLong();
			instance1.UnSerialize(test_in);
			Assert.That(instance1.Header, Is.EqualTo(-2));
			Assert.That(instance1.ID, Is.EqualTo(0x00540013));
			Assert.That(instance1.Total, Is.EqualTo(9));
			Assert.That(instance1.Number, Is.EqualTo(1));
			Assert.That(instance1.Size, Is.EqualTo(0x04E0));
			Assert.That(instance1.Payload.Length, Is.EqualTo(8));
			Assert.That(instance1.Payload.ToArray(), Is.EqualTo(new byte[]
			{
				0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF // Payload
			}));

			// Chaining Serialize / UnSerialize
			var test_msg2 = new PacketLong
			{
				ID = 145,
				Total = 2,
				Number = 1,
				Payload = new MemoryStream(new byte[]
				{
					0x58, 0xAB, 0x45, 0xfe
				})
			};
			var test_msg3 = new PacketLong();
			test_msg3.UnSerialize(test_msg2.Serialize());
			Assert.That(test_msg3.Header, Is.EqualTo(-2));
			Assert.That(test_msg3.ID, Is.EqualTo(145));
			Assert.That(test_msg3.Total, Is.EqualTo(2));
			Assert.That(test_msg3.Number, Is.EqualTo(1));
			Assert.That(test_msg3.Size, Is.EqualTo(0x04E0));
			Assert.That(test_msg3.Payload.ToArray(), Is.EqualTo(new byte[]
			{
				0x58, 0xAB, 0x45, 0xfe // Payload
			}));

			// ID & ID_compressed logic
			test_msg2.ID = 0x01234567;
			test_msg3.UnSerialize(test_msg2.Serialize());
			Assert.That(test_msg3.ID, Is.EqualTo(0x01234567));
			test_msg2.ID_compressed = true;
			test_msg3.UnSerialize(test_msg2.Serialize());
			Assert.That((uint)test_msg3.ID, Is.EqualTo(0x81234567));
			test_msg2.ID_compressed = false;
			test_msg3.UnSerialize(test_msg2.Serialize());
			Assert.That((uint)test_msg3.ID, Is.EqualTo(0x01234567));
			test_msg2.ID_compressed = true;
			test_msg2.ID = 0x01233560;
			test_msg3.UnSerialize(test_msg2.Serialize());
			Assert.That((uint)test_msg3.ID, Is.EqualTo(0x81233560));
		}

		[Test]
		public void PacketShort()
		{
			var instance1 = new PacketShort();

			// Serialize (Default Values)
			var test_out = instance1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0xFF, 0xFF, 0xFF, 0xFF, // Header (-1)
			}));

			// Serialize (With Payload)
			instance1.Payload = new MemoryStream(new byte[]
			{
				0x01, 0x23, 0x45, 0x67
			});
			test_out = instance1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0xFF, 0xFF, 0xFF, 0xFF, // Header (-1)
				0x01, 0x23, 0x45, 0x67 // Payload
			}));

			// UnSerialize (Simple)
			var test_in = new MemoryStream(new byte[]
			{
				0xFF, 0xFF, 0xFF, 0xFF, // Header (-1)
			});
			instance1 = new PacketShort();
			instance1.UnSerialize(test_in);
			Assert.That(instance1.Header, Is.EqualTo(-1));
			Assert.That(instance1.Payload.Length, Is.EqualTo(0));

			// UnSerialize (With Payload)
			test_in = new MemoryStream(new byte[]
			{
				0xFF, 0xFF, 0xFF, 0xFF, // Header (-1)
				0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF // Payload
			});
			instance1 = new PacketShort();
			instance1.UnSerialize(test_in);
			Assert.That(instance1.Header, Is.EqualTo(-1));
			Assert.That(instance1.Payload.Length, Is.EqualTo(8));
			Assert.That(instance1.Payload.ToArray(), Is.EqualTo(new byte[]
			{
				0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF // Payload
			}));

			// Chaining Serialize / UnSerialize
			var test_msg2 = new PacketShort()
			{
				Payload = new MemoryStream(new byte[]
				{
					0x58, 0xAB, 0x45, 0xfe
				})
			};
			var test_msg3 = new PacketShort();
			test_msg3.UnSerialize(test_msg2.Serialize());
			Assert.That(test_msg3.Header, Is.EqualTo(-1));
			Assert.That(test_msg3.Payload.ToArray(), Is.EqualTo(new byte[]
			{
				0x58, 0xAB, 0x45, 0xfe // Payload
			}));
		}
	}
	#endregion

	#region Messages
	[TestFixture]
	sealed class QueryStatsTest_Messages
	{
		[SetUp]
		public void TestSetUp()
		{
			MessageFactory.GetFactory().CustomMessages.Clear();
		}

		[TearDown]
		public void TestTearDown()
		{
			MessageFactory.GetFactory().CustomMessages.Clear();
		}

		[Test]
		public void Fatory_CustomMessage()
		{
			Assert.Throws<ExceptionMessageNotIdentifyed>(() => Fatory_TestMessage<NewCustomMessage>(new byte[] { 0x42 }));

			// Add Custom message to the factory
			MessageFactory.GetFactory().CustomMessages.Add(new NewCustomMessage());

			Fatory_TestMessage<NewCustomMessage>(new byte[]
			{
				0x42, // Header
			});
		}

		[Test]
		public void Fatory()
		{
			// Get factory and test Singleton
			var factory = MessageFactory.GetFactory();
			var factory2 = MessageFactory.GetFactory();
			Assert.AreSame(factory, factory2);

			Fatory_TestMessage<A2S_INFO>(new byte[]
			{
				0x54, // Header
				(byte)'M', (byte)'y', (byte)'T', (byte)'e', (byte)'s', (byte)'t', 0x00, // Payload
			});
			Fatory_TestMessage<A2S_INFO>(new byte[]
			{
				0x54, // Header
				(byte)'M', (byte)'y', (byte)'T', (byte)'e', (byte)'s', (byte)'t', 0x00, // Payload
				0x98, 0x86, 0x23, 0x73, // Challenge
			});
			Fatory_TestMessage<S2A_INFO>(new byte[]
			{
				0x49, // Header
				48, // Protocol
				0x00, // Name
				0x00, // Map
				0x00, // Folder
				0x00, // Game
				0x00, 0x00, // ID
				0x00, // Players
				0x00, // MaxPlayers
				0x00, // Bots
				(byte)'d', // ServerType
				(byte)'w', // Environment
				0x00, // Visibility
				0x00, // VAC
				0x00, // Version
				0x00 // ExtraDataFlag
			});
			Fatory_TestMessage<S2A_PLAYER>(new byte[]
			{
				0x44, // Header
				2, // NbPlayers
				0x00, (byte)'P', (byte)'1', 0x00, 42, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0xbf, // Player Record (Duration = -1f)
				0x01, (byte)'P', (byte)'2', 0x00, 43, 0x00, 0x00, 0x00, 0x02, 0x2b, 0xa7, 0x3e,	// Player Record (Duration = 0.3265f)
			});
			Fatory_TestMessage<A2S_PLAYER>(new byte[]
			{
				0x55, // Header
			});
			Fatory_TestMessage<A2S_PLAYER>(new byte[]
			{
				0x55, // Header
				0x98, 0x86, 0x23, 0x73, // Challenge
			});
			Fatory_TestMessage<A2S_RULES>(new byte[]
			{
				0x56, // Header
			});
			Fatory_TestMessage<A2S_RULES>(new byte[]
			{
				0x56, // Header
				0x98, 0x86, 0x23, 0x73, // Challenge
			});
			Fatory_TestMessage<S2A_RULES>(new byte[]
			{
				0x45, // Header
				0x02, 0x00, // NbRules
				(byte)'K', (byte)'1', 0x00, (byte)'V', (byte)'1', 0x00, // Rule Record
				(byte)'K', (byte)'2', 0x00, (byte)'V', (byte)'2', 0x00,	// Rule Record
			});
			Fatory_TestMessage<S2C_CHALLENGE>(new byte[]
			{
				0x41, // Header
				0x98, 0x86, 0x23, 0x73, // Challenge
			});
		}

		public static void Fatory_TestMessage<T>(byte[] test_data) where T : Message
		{
			var factory = MessageFactory.GetFactory();
			var msg = factory.GetMessage(new MemoryStream(test_data));
			Assert.IsInstanceOf<T>(msg);
			var test_out = msg.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(test_data));
		}

		[Test]
		public void A2S_PLAYER()
		{
			A2S_SimpleCommand<A2S_PLAYER>(0x55, true);
		}

		[Test]
		public void A2S_RULES()
		{
			A2S_SimpleCommand<A2S_RULES>(0x56, true);
		}

		public static void A2S_SimpleCommand<T>(int idMsg, bool bSimpleMessage = false) where T : A2S_SimpleCommand, new()
		{
			var instance1 = new T();
			Assert.That(instance1.Challenge, Is.Null);

			// Serialize (Default Values)
			var test_out = instance1.Serialize();
			Assert.That(test_out.ToArray().Take(1), Is.EqualTo(new byte[]
			{
				(byte)idMsg, // Header
			}));

			// Serialize (With Challenge)
			instance1.Challenge = 0x56847812;
			test_out = instance1.Serialize();
			var res_array = test_out.ToArray();
			Assert.That(res_array.Take(1), Is.EqualTo(new byte[]
			{
				(byte)idMsg, // Header
			}));

			Assert.That(res_array.Skip(res_array.Length - 4).Take(4), Is.EqualTo(new byte[]
			{
				0x12, 0x78, 0x84, 0x56, // Challenge
			}));

			// UnSerialize (Simple)
			var test_in = new MemoryStream(new byte[]
			{
				(byte)idMsg, // Header
			});
			instance1.UnSerialize(test_in);
			Assert.That(instance1.Header, Is.EqualTo(idMsg));
			Assert.That(instance1.Challenge, Is.Null);

			// We cannot predict wich message specific data will be inserted in the Raw message
			if (bSimpleMessage)
			{
				// UnSerialize (WITH Challenge)
				test_in = new MemoryStream(new byte[]
				{
					(byte)idMsg, // Header
					0x12, 0x34, 0x56, 0x78, // Challenge
				});
				instance1.UnSerialize(test_in);
				Assert.That(instance1.Header, Is.EqualTo(idMsg));
				Assert.That(instance1.Challenge, Is.EqualTo(0x78563412));
			}

			// Chaining Serialize / UnSerialize
			var test_msg2 = new T
			{
				Challenge = 0x57841963
			};
			var test_msg3 = new T();
			test_msg3.UnSerialize(test_msg2.Serialize());
			Assert.That(test_msg3.Header, Is.EqualTo(idMsg));
			Assert.That(test_msg3.Challenge, Is.EqualTo(0x57841963));
		}

		[Test]
		public static void S2C_CHALLENGE()
		{
			var instance1 = new S2C_CHALLENGE();
			Assert.That(instance1.Challenge, Is.EqualTo(0));

			// Serialize (Default Values)
			var test_out = instance1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0x41, // Header
				0x00, 0x00, 0x00, 0x00, // Challenge
			}));

			// Serialize (With Challenge)
			instance1.Challenge = 0x56847812;
			test_out = instance1.Serialize();
			var res_array = test_out.ToArray();
			Assert.That(res_array, Is.EqualTo(new byte[]
			{
				0x41, // Header
				0x12, 0x78, 0x84, 0x56, // Challenge
			}));

			// UnSerialize (Simple)
			var test_in = new MemoryStream(new byte[]
			{
				0x41, // Header
				0x98, 0x86, 0x23, 0x73, // Challenge
			});
			instance1.UnSerialize(test_in);
			Assert.That(instance1.Header, Is.EqualTo(0x41));
			Assert.That(instance1.Challenge, Is.EqualTo(0x73238698));

			// Chaining Serialize / UnSerialize
			var test_msg2 = new S2C_CHALLENGE
			{
				Challenge = 0x72587169
			};
			var test_msg3 = new S2C_CHALLENGE();
			test_msg3.UnSerialize(test_msg2.Serialize());
			Assert.That(test_msg3.Header, Is.EqualTo(0x41));
			Assert.That(test_msg3.Challenge, Is.EqualTo(0x72587169));
		}

		[Test]
		public void S2A_RULES()
		{
			// Default Message Instantiation
			var test_msg1 = new S2A_RULES();
			Assert.That(test_msg1.Header, Is.EqualTo(0x45));
			Assert.That(test_msg1.Rules, Is.Empty);

			// Serialize (Default Values)
			var test_out = test_msg1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0x45, // Header
				0x00, 0x00,  // NbRules
			}));

			// Serialize (With Players)
			test_msg1.Rules.Add(new S2A_RULE_DATA { Name = "K1", Value = "V1" });
			test_msg1.Rules.Add(new S2A_RULE_DATA { Name = "K2", Value = "V2" });
			test_out = test_msg1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0x45, // Header
				0x02, 0x00,  // NbRules
				(byte)'K', (byte)'1', 0x00, (byte)'V', (byte)'1', 0x00, // Rule Record
				(byte)'K', (byte)'2', 0x00, (byte)'V', (byte)'2', 0x00,	// Rule Record
			}));

			// UnSerialize (Without Players)
			var test_in = new MemoryStream(new byte[]
			{
				0x45, // Header
				0x00, 0x00, // NbRules
			});
			test_msg1.UnSerialize(test_in);
			Assert.That(test_msg1.Header, Is.EqualTo(0x45));
			Assert.That(test_msg1.Rules, Is.Empty);

			// UnSerialize (With Players)
			test_in = new MemoryStream(new byte[]
			{
				0x45, // Header
				0x03, 0x00, // NbRules
				(byte)'K', (byte)'5', 0x00, (byte)'V', (byte)'4', 0x00, // Rule Record
				(byte)'K', (byte)'9', 0x00, (byte)'V', (byte)'3', 0x00, // Rule Record
				(byte)'K', (byte)'7', 0x00, (byte)'V', (byte)'2', 0x00, // Rule Record
			});
			test_msg1.UnSerialize(test_in);
			Assert.That(test_msg1.Header, Is.EqualTo(0x45));
			Assert.That(test_msg1.Rules.Count, Is.EqualTo(3));
			Assert.That(test_msg1.Rules[0].Name, Is.EqualTo("K5"));
			Assert.That(test_msg1.Rules[0].Value, Is.EqualTo("V4"));
			Assert.That(test_msg1.Rules[1].Name, Is.EqualTo("K9"));
			Assert.That(test_msg1.Rules[1].Value, Is.EqualTo("V3"));
			Assert.That(test_msg1.Rules[2].Name, Is.EqualTo("K7"));
			Assert.That(test_msg1.Rules[2].Value, Is.EqualTo("V2"));

			// Chaining Serialize / UnSerialize
			var test_msg2 = new S2A_RULES();
			test_msg2.Rules.Add(new S2A_RULE_DATA { Name = "K5", Value = "V7" });
			test_msg2.Rules.Add(new S2A_RULE_DATA { Name = "K6", Value = "V9" });
			var test_msg3 = new S2A_RULES();
			test_msg3.UnSerialize(test_msg2.Serialize());
			Assert.That(test_msg3.Header, Is.EqualTo(0x45));
			Assert.That(test_msg3.Rules.Count, Is.EqualTo(2));
			Assert.That(test_msg3.Rules[0].Name, Is.EqualTo("K5"));
			Assert.That(test_msg3.Rules[0].Value, Is.EqualTo("V7"));
			Assert.That(test_msg3.Rules[1].Name, Is.EqualTo("K6"));
			Assert.That(test_msg3.Rules[1].Value, Is.EqualTo("V9"));
		}

		[Test]
		public void S2A_PLAYER()
		{
			// Default Message Instantiation
			var test_msg1 = new S2A_PLAYER();
			Assert.That(test_msg1.Header, Is.EqualTo(0x44));
			Assert.That(test_msg1.Players, Is.Empty);

			// Serialize (Default Values)
			var test_out = test_msg1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0x44, // Header
				0x00, // NbPlayers
			}));

			// Serialize (With Players)
			test_msg1.Players.Add(new S2A_PLAYER_DATA { Index = 0, PlayerName = "P1", Score = 42, Duration = -1f });
			test_msg1.Players.Add(new S2A_PLAYER_DATA { Index = 1, PlayerName = "P2", Score = 43, Duration = 0.3265f });
			test_out = test_msg1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0x44, // Header
				2, // NbPlayers
				0x00, (byte)'P', (byte)'1', 0x00, 42, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0xbf, // Player Record (Duration = -1f)
				0x01, (byte)'P', (byte)'2', 0x00, 43, 0x00, 0x00, 0x00, 0x02, 0x2b, 0xa7, 0x3e,	// Player Record (Duration = 0.3265f)
			}));

			// UnSerialize (Without Players)
			var test_in = new MemoryStream(new byte[]
			{
				0x44, // Header
				0, // NbPlayers
			});
			test_msg1.UnSerialize(test_in);
			Assert.That(test_msg1.Header, Is.EqualTo(0x44));
			Assert.That(test_msg1.Players, Is.Empty);

			// UnSerialize (With Players)
			test_in = new MemoryStream(new byte[]
			{
				0x44, // Header
				3, // NbPlayers
				0x00, (byte)'P', (byte)'5', 0x00, 2, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0xbf, // Player Record (Duration = -1f)
				0x01, (byte)'P', (byte)'6', 0x00, 9, 0x00, 0x00, 0x00, 0x00, 0x00, 0xf6, 0x42,	// Player Record (Duration = 123f)
				0x02, (byte)'P', (byte)'7', 0x00, 7, 0x00, 0x00, 0x00, 0xae, 0xd3, 0x13, 0x45,	// Player Record (Duration = 2365.23f)
			});
			test_msg1.UnSerialize(test_in);
			Assert.That(test_msg1.Header, Is.EqualTo(0x44));
			Assert.That(test_msg1.Players.Count, Is.EqualTo(3));
			Assert.That(test_msg1.Players[0].Index, Is.EqualTo(0));
			Assert.That(test_msg1.Players[0].PlayerName, Is.EqualTo("P5"));
			Assert.That(test_msg1.Players[0].Score, Is.EqualTo(2));
			Assert.That(test_msg1.Players[0].Duration, Is.EqualTo(-1f));
			Assert.That(test_msg1.Players[1].Index, Is.EqualTo(1));
			Assert.That(test_msg1.Players[1].PlayerName, Is.EqualTo("P6"));
			Assert.That(test_msg1.Players[1].Score, Is.EqualTo(9));
			Assert.That(test_msg1.Players[1].Duration, Is.EqualTo(123f));
			Assert.That(test_msg1.Players[2].Index, Is.EqualTo(2));
			Assert.That(test_msg1.Players[2].PlayerName, Is.EqualTo("P7"));
			Assert.That(test_msg1.Players[2].Score, Is.EqualTo(7));
			Assert.That(test_msg1.Players[2].Duration, Is.EqualTo(2365.23f));

			// Chaining Serialize / UnSerialize
			var test_msg2 = new S2A_PLAYER();
			test_msg2.Players.Add(new S2A_PLAYER_DATA { Index = 0, PlayerName = "P65", Score = 654, Duration = -1.23f });
			test_msg2.Players.Add(new S2A_PLAYER_DATA { Index = 1, PlayerName = "Mee", Score = 365, Duration = 6.3565f });
			var test_msg3 = new S2A_PLAYER();
			test_msg3.UnSerialize(test_msg2.Serialize());
			Assert.That(test_msg3.Header, Is.EqualTo(0x44));
			Assert.That(test_msg3.Players.Count, Is.EqualTo(2));
			Assert.That(test_msg3.Players[0].Index, Is.EqualTo(0));
			Assert.That(test_msg3.Players[0].PlayerName, Is.EqualTo("P65"));
			Assert.That(test_msg3.Players[0].Score, Is.EqualTo(654));
			Assert.That(test_msg3.Players[0].Duration, Is.EqualTo(-1.23f));
			Assert.That(test_msg3.Players[1].Index, Is.EqualTo(1));
			Assert.That(test_msg3.Players[1].PlayerName, Is.EqualTo("Mee"));
			Assert.That(test_msg3.Players[1].Score, Is.EqualTo(365));
			Assert.That(test_msg3.Players[1].Duration, Is.EqualTo(6.3565f));
		}

		[Test]
		public void A2S_INFO()
		{
			A2S_SimpleCommand<A2S_INFO>(0x54);

			// Default Message Instantiation
			var test_msg1 = new A2S_INFO();
			Assert.That(test_msg1.Header, Is.EqualTo(0x54));
			Assert.That(test_msg1.Payload, Is.EqualTo("Source Engine Query"));
			Assert.That(test_msg1.Challenge, Is.Null);

			// Serialize (Default Values)
			var test_out = test_msg1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0x54, // Header
				(byte)'S', (byte)'o', (byte)'u', (byte)'r', (byte)'c', (byte)'e', (byte)' ',
				(byte)'E', (byte)'n', (byte)'g', (byte)'i' ,(byte)'n' ,(byte)'e', (byte)' ',
				(byte)'Q', (byte)'u', (byte)'e', (byte)'r', (byte)'y', 0x00, // Payload
			}));

			// Serialize (With Payload Values)
			test_msg1.Payload = "MyTest";
			test_out = test_msg1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0x54, // Header
				(byte)'M', (byte)'y', (byte)'T', (byte)'e', (byte)'s', (byte)'t', 0x00, // Payload
			}));

			// Serialize (With Payload Values and Challenge)
			test_msg1.Payload = "MyTest2";
			test_msg1.Challenge = 0x56234598;
			test_out = test_msg1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0x54, // Header
				(byte)'M', (byte)'y', (byte)'T', (byte)'e', (byte)'s', (byte)'t', (byte)'2', 0x00, // Payload
				0x98, 0x45, 0x23, 0x56 // Challenge = 0x56234598
			}));

			// UnSerialize with nominal value (and NO Challenge)
			var test_in = new MemoryStream(new byte[]
			{
				0x54, // Header
				(byte)'t', (byte)'e', (byte)'s', (byte)'t', 0x00 // Payload
			});

			test_msg1.UnSerialize(test_in);

			Assert.That(test_msg1.Header, Is.EqualTo(0x54));
			Assert.That(test_msg1.Payload, Is.EqualTo("test"));
			Assert.That(test_msg1.Challenge, Is.Null);
			Assert.That(test_in.ReadAllBytes().Length, Is.EqualTo(0));

			// UnSerialize with nominal value (and Challenge)
			test_in = new MemoryStream(new byte[]
			{
				0x54, // Header
				(byte)'_', (byte)'t', (byte)'e', (byte)'s', (byte)'t', (byte)'2', 0x00, // Payload
				0x12, 0x34, 0x56, 0x78 // Challenge = 0x78563412
			});
			test_msg1.UnSerialize(test_in);
			Assert.That(test_msg1.Header, Is.EqualTo(0x54));
			Assert.That(test_msg1.Payload, Is.EqualTo("_test2"));
			Assert.That(test_msg1.Challenge, Is.EqualTo(0x78563412));
			Assert.That(test_in.ReadAllBytes().Length, Is.EqualTo(0));

			// UnSerialize with nominal value (and Challenge) 2 (empty payload)
			test_in = new MemoryStream(new byte[]
			{
				0x54, // Header
				0x00, // Payload
				0x78, 0x56, 0x34, 0x12 // Challenge = 0x12345678
			});
			test_msg1.UnSerialize(test_in);
			Assert.That(test_msg1.Header, Is.EqualTo(0x54));
			Assert.That(test_msg1.Payload, Is.EqualTo(""));
			Assert.That(test_msg1.Challenge, Is.EqualTo(0x12345678));
			Assert.That(test_in.ReadAllBytes().Length, Is.EqualTo(0));

			// Chaining Serialize / UnSerialize
			var test_msg2 = new A2S_INFO
			{
				Payload = "MySuperTest"
			};
			var test_msg3 = new A2S_INFO();
			test_msg3.UnSerialize(test_msg2.Serialize());
			Assert.That(test_msg3.Payload, Is.EqualTo("MySuperTest"));
			Assert.That(test_msg3.Challenge, Is.Null);

			// + With challenge
			test_msg2.Challenge = 0x33452376;
			test_msg3.UnSerialize(test_msg2.Serialize());
			Assert.That(test_msg3.Challenge, Is.EqualTo(0x33452376));
		}

		[Test]
		public void S2A_INFO()
		{
			// Default Message Instantiation
			var test_msg1 = new S2A_INFO();
			Assert.That(test_msg1.Header, Is.EqualTo(0x49));
			Assert.That(test_msg1.Protocol, Is.EqualTo(48));
			Assert.That(test_msg1.Name, Is.Empty);
			Assert.That(test_msg1.Map, Is.Empty);
			Assert.That(test_msg1.Folder, Is.Empty);
			Assert.That(test_msg1.Game, Is.Empty);
			Assert.That(test_msg1.ID, Is.EqualTo(0));
			Assert.That(test_msg1.Players, Is.EqualTo(0));
			Assert.That(test_msg1.MaxPlayers, Is.EqualTo(0));
			Assert.That(test_msg1.Bots, Is.EqualTo(0));
			Assert.That(test_msg1.ServerType, Is.EqualTo('d'));
			Assert.That(test_msg1.Environment, Is.EqualTo('w'));
			Assert.That(test_msg1.Visibility, Is.EqualTo(0));
			Assert.That(test_msg1.VAC, Is.EqualTo(0));
			Assert.That(test_msg1.Version, Is.Empty);
			Assert.That(test_msg1.ExtraDataFlag, Is.EqualTo(0));
			Assert.That(test_msg1.Port, Is.Null);
			Assert.That(test_msg1.SteamID, Is.Null);
			Assert.That(test_msg1.SpecPort, Is.Null);
			Assert.That(test_msg1.SpecName, Is.Null);
			Assert.That(test_msg1.Keywords, Is.Null);
			Assert.That(test_msg1.GameID, Is.Null);

			// Serialize (Default Values)
			var test_out = test_msg1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0x49, // Header
				48, // Protocol
				0x00, // Name
				0x00, // Map
				0x00, // Folder
				0x00, // Game
				0x00, 0x00, // ID
				0x00, // Players
				0x00, // MaxPlayers
				0x00, // Bots
				(byte)'d', // ServerType
				(byte)'w', // Environment
				0x00, // Visibility
				0x00, // VAC
				0x00, // Version
				0x00 // ExtraDataFlag
			}));

			// Serialize (With custom Values, WITHOUT ExtraData)
			test_msg1.Name = "NAME";
			test_msg1.Map = "MAP";
			test_msg1.Folder = "FOLDER";
			test_msg1.Game = "GAME";
			test_msg1.ID = 0x6548;
			test_msg1.Players = 2;
			test_msg1.MaxPlayers = 3;
			test_msg1.Bots = 4;
			test_msg1.ServerType = (byte)'l';
			test_msg1.Environment = (byte)'m';
			test_msg1.Visibility = 5;
			test_msg1.VAC = 6;
			test_msg1.Version = "1.0.1";
			test_out = test_msg1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0x49, // Header
				48, // Protocol
				(byte)'N', (byte)'A', (byte)'M', (byte)'E', 0x00, // Name
				(byte)'M', (byte)'A', (byte)'P', 0x00, // Map
				(byte)'F', (byte)'O', (byte)'L', (byte)'D', (byte)'E', (byte)'R', 0x00, // Folder
				(byte)'G', (byte)'A', (byte)'M', (byte)'E', 0x00, // Game
				0x48, 0x65, // ID
				2, // Players
				3, // MaxPlayers
				4, // Bots
				(byte)'l', // ServerType
				(byte)'m', // Environment
				5, // Visibility
				6, // VAC
				(byte)'1', (byte)'.', (byte)'0', (byte)'.', (byte)'1', 0x00, // Version
				0x00 // ExtraDataFlag
			}));

			// Serialize (With custom Values, WITH ExtraData)
			test_msg1.Port = 0x5012;
			test_msg1.SteamID = 0x9875987185784987;
			test_msg1.SpecPort = 0x5298;
			test_msg1.SpecName = "SpecNam1";
			test_msg1.Keywords = "Keyword1";
			test_msg1.GameID = 0x3456786871597395;
			test_out = test_msg1.Serialize();
			Assert.That(test_out.ToArray(), Is.EqualTo(new byte[]
			{
				0x49, // Header
				48, // Protocol
				(byte)'N', (byte)'A', (byte)'M', (byte)'E', 0x00, // Name
				(byte)'M', (byte)'A', (byte)'P', 0x00, // Map
				(byte)'F', (byte)'O', (byte)'L', (byte)'D', (byte)'E', (byte)'R', 0x00, // Folder
				(byte)'G', (byte)'A', (byte)'M', (byte)'E', 0x00, // Game
				0x48, 0x65, // ID
				2, // Players
				3, // MaxPlayers
				4, // Bots
				(byte)'l', // ServerType
				(byte)'m', // Environment
				5, // Visibility
				6, // VAC
				(byte)'1', (byte)'.', (byte)'0', (byte)'.', (byte)'1', 0x00, // Version
				0xF1, // ExtraDataFlag
				0x12, 0x50, // Port
				0x87, 0x49, 0x78, 0x85, 0x71, 0x98, 0x75, 0x98, // SteamID
				0x98, 0x52, // SpecPort
				(byte)'S', (byte)'p', (byte)'e', (byte)'c', (byte)'N', (byte)'a', (byte)'m', (byte)'1', 0x00, // SpecName
				(byte)'K', (byte)'e', (byte)'y', (byte)'w', (byte)'o', (byte)'r', (byte)'d', (byte)'1', 0x00, // Keywords
				0x95, 0x73, 0x59, 0x71, 0x68, 0x78, 0x56, 0x34, // GameID
			}));

			// UnSerialize (With custom Values, WITH ExtraData)
			var test_in = new MemoryStream(new byte[]
			{
				0x49, // Header
				48, // Protocol
				(byte)'T', (byte)'e', (byte)'s', (byte)'t', 0x00, // Name
				(byte)'M', (byte)'a', (byte)'p', 0x00, // Map
				(byte)'F', (byte)'o', (byte)'l', (byte)'d', (byte)'e', (byte)'r', 0x00, // Folder
				(byte)'G', (byte)'a', (byte)'m', (byte)'e', 0x00, // Game
				0x12, 0x34, // ID
				0x05, // Players
				0x0A, // MaxPlayers
				0x02, // Bots
				(byte)'l', // ServerType
				(byte)'w', // Environment
				0x01, // Visibility
				0x01, // VAC
				(byte)'1', (byte)'.', (byte)'0', (byte)'.', (byte)'0', 0x00, // Version
				0xF1, // ExtraDataFlag
				0x23, 0x45, // Port
				0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01, // SteamID
				0x45, 0x67, // SpecPort
				(byte)'S', (byte)'p', (byte)'e', (byte)'c', (byte)'S', (byte)'e', (byte)'r', (byte)'v', (byte)'e', (byte)'r', 0x00, // SpecName
				(byte)'k', (byte)'w', (byte)'1', (byte)',', (byte)'k', (byte)'w', (byte)'2', 0x00, // Keywords
				0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89, // GameID
			});
			var test_msg2 = new S2A_INFO();
			test_msg2.UnSerialize(test_in);
			Assert.That(test_msg2.Header, Is.EqualTo(0x49));
			Assert.That(test_msg2.Protocol, Is.EqualTo(48));
			Assert.That(test_msg2.Name, Is.EqualTo("Test"));
			Assert.That(test_msg2.Map, Is.EqualTo("Map"));
			Assert.That(test_msg2.Folder, Is.EqualTo("Folder"));
			Assert.That(test_msg2.Game, Is.EqualTo("Game"));
			Assert.That(test_msg2.ID, Is.EqualTo(0x3412));
			Assert.That(test_msg2.Players, Is.EqualTo(5));
			Assert.That(test_msg2.MaxPlayers, Is.EqualTo(10));
			Assert.That(test_msg2.Bots, Is.EqualTo(2));
			Assert.That(test_msg2.ServerType, Is.EqualTo((byte)'l'));
			Assert.That(test_msg2.Environment, Is.EqualTo((byte)'w'));
			Assert.That(test_msg2.Visibility, Is.EqualTo(1));
			Assert.That(test_msg2.VAC, Is.EqualTo(1));
			Assert.That(test_msg2.Version, Is.EqualTo("1.0.0"));
			Assert.That(test_msg2.ExtraDataFlag, Is.EqualTo(0xF1));
			Assert.That(test_msg2.Port, Is.EqualTo(0x4523));
			Assert.That(test_msg2.SteamID, Is.EqualTo(0x0123456789ABCDEF));
			Assert.That(test_msg2.SpecPort, Is.EqualTo(0x6745));
			Assert.That(test_msg2.SpecName, Is.EqualTo("SpecServer"));
			Assert.That(test_msg2.Keywords, Is.EqualTo("kw1,kw2"));
			Assert.That(test_msg2.GameID, Is.EqualTo(0x8967452301EFCDAB));

			// Chaining Serialize / UnSerialize
			var test_msg3 = new S2A_INFO
			{
				Name = "NAME65",
				Map = "MAP14",
				Folder = "FOLDER78",
				Game = "GAME14",
				ID = 0x1564,
				Players = 7,
				MaxPlayers = 8,
				Bots = 96,
				ServerType = (byte)'l',
				Environment = (byte)'m',
				Visibility = 98,
				VAC = 65,
				Version = "2.3.1",
				Port = 0x3a5b,
				SteamID = 0x129875a6b5e5f912,
				SpecPort = 0x5298,
				SpecName = "abcd",
				Keywords = "efgh",
				GameID = 0x095a3b5c3f69e563
			};
			var test_msg4 = new S2A_INFO();
			test_msg4.UnSerialize(test_msg3.Serialize());
			Assert.That(test_msg4.Header, Is.EqualTo(0x49));
			Assert.That(test_msg4.Protocol, Is.EqualTo(48));
			Assert.That(test_msg4.Name, Is.EqualTo("NAME65"));
			Assert.That(test_msg4.Map, Is.EqualTo("MAP14"));
			Assert.That(test_msg4.Folder, Is.EqualTo("FOLDER78"));
			Assert.That(test_msg4.Game, Is.EqualTo("GAME14"));
			Assert.That(test_msg4.ID, Is.EqualTo(0x1564));
			Assert.That(test_msg4.Players, Is.EqualTo(7));
			Assert.That(test_msg4.MaxPlayers, Is.EqualTo(8));
			Assert.That(test_msg4.Bots, Is.EqualTo(96));
			Assert.That(test_msg4.ServerType, Is.EqualTo((byte)'l'));
			Assert.That(test_msg4.Environment, Is.EqualTo((byte)'m'));
			Assert.That(test_msg4.Visibility, Is.EqualTo(98));
			Assert.That(test_msg4.VAC, Is.EqualTo(65));
			Assert.That(test_msg4.Version, Is.EqualTo("2.3.1"));
			Assert.That(test_msg4.ExtraDataFlag, Is.EqualTo(0xF1));
			Assert.That(test_msg4.Port, Is.EqualTo(0x3a5b));
			Assert.That(test_msg4.SteamID, Is.EqualTo(0x129875a6b5e5f912));
			Assert.That(test_msg4.SpecPort, Is.EqualTo(0x5298));
			Assert.That(test_msg4.SpecName, Is.EqualTo("abcd"));
			Assert.That(test_msg4.Keywords, Is.EqualTo("efgh"));
			Assert.That(test_msg4.GameID, Is.EqualTo(0x095a3b5c3f69e563));
		}
	}
	#endregion
}
