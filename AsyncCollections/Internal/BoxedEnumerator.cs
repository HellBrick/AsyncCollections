using System.Collections;
using System.Collections.Generic;

namespace HellBrick.Collections.Internal
{
	/// <remarks>Turns out iterating through manually boxed iterator is a bit faster than through automatically boxed one.</remarks>
	internal class BoxedEnumerator<TItem, TEnumerator> : IEnumerator<TItem> where TEnumerator : struct, IEnumerator<TItem>
	{
		private TEnumerator _structEnumerator;

		public BoxedEnumerator( TEnumerator structEnumerator )
		{
			_structEnumerator = structEnumerator;
		}

		public TItem Current => _structEnumerator.Current;
		object IEnumerator.Current => Current;
		public void Dispose() => _structEnumerator.Dispose();
		public bool MoveNext() => _structEnumerator.MoveNext();
		public void Reset() => _structEnumerator.Reset();
	}
}
