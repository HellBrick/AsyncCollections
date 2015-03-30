using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections.Internal
{
	internal static class TaskExtensions
	{
		public static async Task<T> WithThreadAbortedFlag<T>( this Task<T> task )
		{
			await Task.Yield();
			return await task;
		}
	}
}
