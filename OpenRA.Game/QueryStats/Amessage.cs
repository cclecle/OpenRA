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
using System.Collections.Immutable;
using System.IO;
using System.Text;

namespace OpenRA.QueryStats
{
	public abstract class ExceptionAMessage : Exception { }
	public class ExceptionMessageNotIdentifyed : ExceptionAMessage { }
	public abstract class AMessageFactory<T> where T : AMessageFactory<T>, new()
	{
		public virtual AMessage[] RegisteredMessages { get; }

		protected AMessageFactory() { }
		static T Factory { get; set; } = null;
		static readonly object FactoryLock = new();
		public static T GetFactory()
		{
			if (Factory == null)
			{
				lock (FactoryLock)
				{
					Factory ??= new T();
				}
			}

			return Factory;
		}

		public AMessage GetMessage(MemoryStream test_stream)
		{
			foreach (var msg_ref in RegisteredMessages)
			{
				if (msg_ref.Identify(test_stream))
				{
					var new_msg = (AMessage)Activator.CreateInstance(msg_ref.GetType());
					new_msg.UnSerialize(test_stream);
					return new_msg;
				}
			}

			throw new ExceptionMessageNotIdentifyed();
		}
	}

	public abstract class AMessage : ISerializableMessage
	{
		protected MemoryStream stream = null;
		protected BinaryReader reader = null;
		protected BinaryWriter writer = null;
		readonly object serializeLock = new();
		public override string ToString()
		{
			return $"## Message : {GetType().Name} : {GetHashCode()}";
		}

		public abstract bool Identify(MemoryStream test_stream);
		protected abstract void SerializeHeader();
		protected virtual void SerializePayload() { }

		public MemoryStream Serialize()
		{
			lock (serializeLock)
			{
				var local_stream = new MemoryStream();
				stream = local_stream;
				using (writer = new BinaryWriter(local_stream, new UTF8Encoding(), true))
				{
					SerializeHeader();
					SerializePayload();
				}

				writer = null;
				stream = null;

				local_stream.Seek(0, SeekOrigin.Begin);
				return local_stream;
			}
		}

		protected abstract void UnSerializeHeader();
		protected virtual void UnSerializePayload() { }
		public void UnSerialize(MemoryStream local_stream)
		{
			lock (serializeLock)
			{
				stream = local_stream;
				using (reader = new BinaryReader(local_stream, new UTF8Encoding(), true))
				{
					UnSerializeHeader();
					UnSerializePayload();
				}

				reader = null;
				stream = null;
			}
		}
	}
}
