using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections.Internal
{
	internal struct SelectManyStructEnumererator<TSource, TSourceEnumerator, TTarget, TTargetEnumerator> : IEnumerator<TTarget>
		where TSourceEnumerator : struct, IEnumerator<TSource>
		where TTargetEnumerator : struct, IEnumerator<TTarget>
	{
		private readonly Func<TSource, TTargetEnumerator> _selector;
		private TSourceEnumerator _sourceEnumerator;

		private TTargetEnumerator _currentTargetEnumerator;
		private bool _hasCurrentTarget;

		public SelectManyStructEnumererator( TSourceEnumerator sourceEnumerator, Func<TSource, TTargetEnumerator> selector )
		{
			_sourceEnumerator = sourceEnumerator;
			_selector = selector;

			_hasCurrentTarget = false;
			_currentTargetEnumerator = default( TTargetEnumerator );
			Current = default( TTarget );
		}

		public TTarget Current { get; private set; }
		object IEnumerator.Current => Current;

		public bool MoveNext()
		{
			do
			{
				if ( !_hasCurrentTarget && !TryMoveToNextTarget() )
					return false;

				if ( _currentTargetEnumerator.MoveNext() )
				{
					Current = _currentTargetEnumerator.Current;
					return true;
				}
			}
			while ( TryMoveToNextTarget() );

			return false;
		}

		private bool TryMoveToNextTarget()
		{
			TryDisposeCurrentTarget();

			_hasCurrentTarget = _sourceEnumerator.MoveNext();
			if ( _hasCurrentTarget )
				_currentTargetEnumerator = _selector( _sourceEnumerator.Current );

			return _hasCurrentTarget;
		}

		private void TryDisposeCurrentTarget()
		{
			if ( _hasCurrentTarget )
				_currentTargetEnumerator.Dispose();
		}

		public void Dispose() => TryDisposeCurrentTarget();

		public void Reset()
		{
		}
	}
}
