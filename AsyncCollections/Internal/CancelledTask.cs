using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections.Internal
{
	static class CanceledTask<T>
	{
		static CanceledTask()
		{
			TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
			tcs.SetCanceled();
			Value = tcs.Task;
		}

		public static readonly Task<T> Value;
	}
}
