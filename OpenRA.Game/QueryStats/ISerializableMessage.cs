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

namespace OpenRA.QueryStats
{
	public interface ISerializableMessage
	{
		/// <summary>
		/// Serialize the object to a MemoryStream.
		/// The stream is rewound to the beginning before being returned.
		/// </summary>
		MemoryStream Serialize();

		/// <summary>
		/// UnSerialize the object from a MemoryStream.
		/// The given stream is used at its current position.
		/// </summary>
		void UnSerialize(MemoryStream stream);
	}
}
