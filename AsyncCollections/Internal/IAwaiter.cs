using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections.Internal
{
	/// <summary>
	/// Represents an abstract item awaiter.
	/// </summary>
	internal interface IAwaiter<T>
	{
		/// <summary>
		/// <para>Attempts to complete the awaiter with a specified result.</para>
		/// <para>Returns false if the awaiter has been canceled.</para>
		/// </summary>
		bool TrySetResult( T result );

		/// <summary>
		/// The task that's completed when the awaiter gets the result.
		/// </summary>
		ValueTask<T> Task { get; }
	}
}
