using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HellBrick.Collections.Internal
{
	internal struct BitArray32 : IEquatable<BitArray32>
	{
		public const int BitCapacity = sizeof( uint ) * 8;
		public static readonly BitArray32 Empty = new BitArray32();

		private readonly uint _value;

		public BitArray32( uint value )
		{
			_value = value;
		}

		public BitArray32 WithBitSet( int index ) => new BitArray32( _value | GetMask( index ) );

		public bool IsBitSet( int index )
		{
			uint mask = GetMask( index );
			return ( _value & mask ) == mask;
		}

		public override string ToString()
		{
			char[] chars = new char[ BitCapacity ];

			for ( int index = 0; index < BitCapacity; index++ )
			{
				char bitChar = IsBitSet( index ) ? '1' : '0';
				chars[ index ] = bitChar;
			}

			return new string( chars );
		}

		private static uint GetMask( int index ) => ( 1u << index );

		#region IEquatable<BitArray32>

		public override int GetHashCode() => EqualityComparer<uint>.Default.GetHashCode( _value );
		public bool Equals( BitArray32 other ) => EqualityComparer<uint>.Default.Equals( _value, other._value );
		public override bool Equals( object obj ) => obj is BitArray32 && Equals( (BitArray32) obj );

		public static bool operator ==( BitArray32 x, BitArray32 y ) => x.Equals( y );
		public static bool operator !=( BitArray32 x, BitArray32 y ) => !x.Equals( y );

		#endregion
	}
}
