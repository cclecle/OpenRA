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
using System.Linq;
using System.Net;

namespace OpenRA.QueryStats
{
	public abstract class ExceptionSession : Exception { }
	public class UnsupportedMessage : ExceptionSession { }
	public class WrongPacketSequence : ExceptionSession { }
	public abstract class ServerSessionException : ExceptionSession { }
	public class WrongChallenge : ServerSessionException { }
	public class WrongA2S_INFOPayload : ServerSessionException { }
	public abstract class ClientSessionException : ExceptionSession { }
	public class NoPendingCommand : ClientSessionException { }
	public class WrongServerResponse : ClientSessionException { }
	public class PreviousCommandNotCompleted : ClientSessionException { }
	public class ChallengeShouldNotBeSet : ClientSessionException { }
	public abstract class Session
	{
		protected int? challenge;
		protected Frame pendingFrame = null;
		protected object pendingFrameLock = new();
		protected DateTime creationDateTime = DateTime.Now;
		public DateTime LastUpdatedDateTime { get; protected set; } = DateTime.Now;
		public bool DontDelete = false;

		public Frame ConsumePacket(Packet pkt)
		{
			Frame result = null;
			lock (pendingFrameLock)
			{
				if (pendingFrame != null)
				{
					if (pkt is PacketLong packetLong)
					{
						try
						{
							pendingFrame.AddPacket(packetLong);
						}
						catch (Exception)
						{
							pendingFrame = null;
							throw;
						}
					}
					else
					{
						pendingFrame = null;
						throw new WrongPacketSequence();
					}
				}
				else
				{
					pendingFrame = new Frame(pkt);
				}

				if (pendingFrame.Completed)
				{
					try
					{
						result = OnMessageReceived(pendingFrame.Msg);
						LastUpdatedDateTime = DateTime.Now;
					}
					finally
					{
						pendingFrame = null;
					}
				}
			}

			return result;
		}

		protected abstract Frame OnMessageReceived(Message message);
	}

	public abstract class ServerSession : Session
	{
		public bool CheckA2SINFOPayload { get; set; } = true;
		public string ExpectedA2SINFOPayload { get; protected set; } = A2S_INFO.DefaultPayload;
		public TimeSpan SessionChallengeTTLUpdate { get; protected set; } = TimeSpan.FromSeconds(30);
		public TimeSpan SessionChallengeTTLCreation { get; protected set; } = TimeSpan.FromMinutes(5);
		protected DateTime challengeCreationDateTime = DateTime.Now;
		protected static readonly Random Rnd = new((int)DateTime.Now.Ticks & 0x0000FFFF);
		protected static int GetNewChallenge()
		{
			int newval;
			do
			{
				newval = Rnd.Next();
			}
			while (newval == -1);
			return newval;
		}

		protected virtual Frame ReNewChallenge()
		{
			challenge = GetNewChallenge();
			challengeCreationDateTime = DateTime.Now;
			return new Frame(new S2C_CHALLENGE() { Challenge = (int)challenge });
		}

		protected abstract Frame Impl_Process_A2S_INFO(A2S_INFO a2S_INFO);

		protected abstract Frame Impl_Process_A2S_RULES(A2S_RULES a2S_RULES);

		protected abstract Frame Impl_Process_A2S_PLAYER(A2S_PLAYER aA2S_PLAYER);

		protected virtual Frame ProcessCommand(A2S_SimpleCommand a2S_SimpleCommand)
		{
			if (a2S_SimpleCommand is A2S_INFO a2S_INFO)
			{
				if (CheckA2SINFOPayload && a2S_INFO.Payload != ExpectedA2SINFOPayload)
					throw new WrongA2S_INFOPayload();
				return Impl_Process_A2S_INFO(a2S_INFO);
			}
			else if (a2S_SimpleCommand is A2S_RULES a2S_RULES)
			{
				return Impl_Process_A2S_RULES(a2S_RULES);
			}
			else if (a2S_SimpleCommand is A2S_PLAYER a2S_PLAYER)
			{
				return Impl_Process_A2S_PLAYER(a2S_PLAYER);
			}
			else
			{
				throw new UnsupportedMessage();
			}
		}

		protected override Frame OnMessageReceived(Message message)
		{
			if ((challenge is null) ||
				(DateTime.Now - LastUpdatedDateTime > SessionChallengeTTLUpdate) ||
				(DateTime.Now - challengeCreationDateTime > SessionChallengeTTLCreation))
			{
				return ReNewChallenge();
			}
			else
			{
				if (message is A2S_SimpleCommand a2S_SimpleCommand)
				{
					if (a2S_SimpleCommand.Challenge != null)
					{
						if (a2S_SimpleCommand.Challenge == challenge)
						{
							return ProcessCommand(a2S_SimpleCommand);
						}
						else if (a2S_SimpleCommand.Challenge == -1)
						{
							return ReNewChallenge();
						}
						else
						{
							throw new WrongChallenge();
						}
					}
					else
					{
						return ReNewChallenge();
					}
				}
				else
				{
					throw new UnsupportedMessage();
				}
			}
		}
	}

	public class ClientSession : Session
	{
		protected A2S_SimpleCommand pendingCommand = null;
		public Message Response = null;
		public bool ResponseOk = false;

		protected virtual void OnCommandCompleted(A2S_SimpleCommand command) { }

		protected virtual Frame ProcessResponse(Message message)
		{
			var command = pendingCommand;
			try
			{
				if (message is S2A_INFO)
				{
					if (pendingCommand is not A2S_INFO)
						throw new WrongServerResponse();
				}
				else if (message is S2A_RULES)
				{
					if (pendingCommand is not A2S_RULES)
						throw new WrongServerResponse();
				}
				else if (message is S2A_PLAYER)
				{
					if (pendingCommand is not A2S_PLAYER)
						throw new WrongServerResponse();
				}
				else
				{
					throw new UnsupportedMessage();
				}
			}
			finally
			{
				pendingCommand = null;
			}

			ResponseOk = true;
			Response = message;
			OnCommandCompleted(command);

			return null;
		}

		protected override Frame OnMessageReceived(Message message)
		{
			if (pendingCommand == null)
			{
				throw new NoPendingCommand();
			}

			if (message is S2C_CHALLENGE s2C_CHALLENGE)
			{
				challenge = s2C_CHALLENGE.Challenge;
				pendingCommand.Challenge = challenge;
				return new Frame(pendingCommand);
			}
			else
			{
				return ProcessResponse(message);
			}
		}

		public void ResetCommand()
		{
			ResponseOk = false;
			Response = null;
			pendingCommand = null;
		}

		public virtual Frame SendCommand(A2S_SimpleCommand a2S_SimpleCommand)
		{
			if (pendingCommand != null)
			{
				throw new PreviousCommandNotCompleted();
			}

			if (a2S_SimpleCommand.Challenge != null)
			{
				throw new ChallengeShouldNotBeSet();
			}

			ResetCommand();

			pendingCommand = a2S_SimpleCommand;

			if (challenge != null)
			{
				a2S_SimpleCommand.Challenge = challenge;
			}

			return new Frame(a2S_SimpleCommand);
		}
	}

	public abstract class SessionHandler<TSession> where TSession : Session, new()
	{
		public TimeSpan SessionTTLUpdate { get; protected set; } = TimeSpan.FromMinutes(5);
		protected IDictionary<IPEndPoint, TSession> activeSessions = new Dictionary<IPEndPoint, TSession>();

		protected void PurgeOldSessions()
		{
			lock (activeSessions)
			{
				foreach (var session in activeSessions.Where(s => !s.Value.DontDelete))
				{
					if (DateTime.Now - session.Value.LastUpdatedDateTime > SessionTTLUpdate)
					{
						activeSessions.Remove(session);
					}
				}
			}
		}

		protected virtual TSession CreateSession()
		{
			return new TSession();
		}

		public TSession GetSession(IPEndPoint remoteEndPoint)
		{
			return activeSessions[remoteEndPoint];
		}

		protected TSession GetOrCreateSession(IPEndPoint remoteEndPoint)
		{
			TSession session;

			lock (activeSessions)
			{
				if (activeSessions.ContainsKey(remoteEndPoint))
				{
					session = activeSessions[remoteEndPoint];
				}
				else
				{
					session = CreateSession();
					activeSessions.Add(remoteEndPoint, session);
				}

				session.DontDelete = true;
			}

			return session;
		}

		public Frame ConsumePacket(IPEndPoint remoteEndPoint, Packet pkt)
		{
			TSession session;

			lock (activeSessions)
			{
				PurgeOldSessions();
				session = GetOrCreateSession(remoteEndPoint);
			}

			try
			{
				return session.ConsumePacket(pkt);
			}
			finally
			{
				session.DontDelete = false;
			}
		}
	}

	public abstract class ServerSessionHandler<TServerSession> : SessionHandler<TServerSession> where TServerSession : ServerSession, new() { }
	public abstract class ClientSessionHandler<TClientSession> : SessionHandler<TClientSession> where TClientSession : ClientSession, new()
	{
		public virtual Frame SendCommand(IPEndPoint remoteEndPoint, A2S_SimpleCommand a2S_SimpleCommand)
		{
			TClientSession session;

			lock (activeSessions)
			{
				PurgeOldSessions();
				session = GetOrCreateSession(remoteEndPoint);
			}

			try
			{
				return session.SendCommand(a2S_SimpleCommand);
			}
			finally
			{
				session.DontDelete = false;
			}
		}
	}

	public class SimpleClientSessionHandler : ClientSessionHandler<ClientSession> { }
}
