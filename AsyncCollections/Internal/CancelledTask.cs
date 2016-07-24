using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections.Internal
{
	internal static class CanceledValueTask<T>
	{
		public static readonly ValueTask<T> Value = CreateCanceledTask();

		private static ValueTask<T> CreateCanceledTask()
		{
			TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
			tcs.SetCanceled();
			return new ValueTask<T>( tcs.Task );
		}
	}
}
