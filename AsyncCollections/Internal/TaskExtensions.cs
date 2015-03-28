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
		private static readonly Action<Task> _stateSetter = GenerateStateSetter();

		private static Action<Task> GenerateStateSetter()
		{
			const int taskStateThreadWasAborted = 0x8000000;
			FieldInfo stateField = typeof( Task ).GetTypeInfo().GetDeclaredField( "m_stateFlags" );

			ParameterExpression taskParameter = Expression.Parameter( typeof( Task ) );
			var setterExpression =
				Expression.Assign(
					Expression.Field( taskParameter, stateField ),
					Expression.Or(
						Expression.Field( taskParameter, stateField ),
						Expression.Constant( taskStateThreadWasAborted ) ) );

			return Expression.Lambda<Action<Task>>( setterExpression, taskParameter ).Compile();
		}

		public static Task<T> WithThreadAbortedFlag<T>( this Task<T> task )
		{
			_stateSetter( task );
			return task;
		}
	}
}
