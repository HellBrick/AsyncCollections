using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Collections;

namespace HellBrick.Collections.Test
{
	public static class Assertions
	{
		/// <summary>
		/// Expects the current collection to contain all elements of <paramref name="expected"/> in that exact order.
		/// </summary>
		public static AndConstraint<GenericCollectionAssertions<T>> BeEqualTo<T>( this GenericCollectionAssertions<T> collectionShould, IReadOnlyList<T> expected )
		{
			AndConstraint<GenericCollectionAssertions<T>> andConstraint = collectionShould.HaveSameCount( expected );

			for ( int i = 0; i < expected.Count; i++ )
				andConstraint = collectionShould.HaveElementAt( i, expected[ i ] );

			return andConstraint;
		}
	}
}
