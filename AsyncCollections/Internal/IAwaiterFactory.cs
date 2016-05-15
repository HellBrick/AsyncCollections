using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections.Internal
{
	internal interface IAwaiterFactory<T>
	{
		IAwaiter<T> CreateAwaiter();
	}
}
