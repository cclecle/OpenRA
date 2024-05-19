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

using System.IO;
using System.Text;

namespace OpenRA.Support
{
	public static class MemoryStreamTools
	{
		public static string ReadNullTerminated(this BinaryReader rdr)
		{
			var bldr = new StringBuilder();
			int nc;
			while ((nc = rdr.Read()) > 0)
				bldr.Append((char)nc);

			return bldr.ToString();
		}

		public static void WriteNullTerminated(this BinaryWriter wrt, string input)
		{
			wrt.Write(Encoding.ASCII.GetBytes(input));
			wrt.Write((byte)0);
		}
	}
}
