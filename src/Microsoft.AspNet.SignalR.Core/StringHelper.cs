using System;
using System.Runtime.CompilerServices;
using static System.Net.Mime.MediaTypeNames;

namespace Microsoft.AspNet.SignalR
{
    public static class StringHelper
    {
#if !NETCOREAPP
        public static bool Contains( this string str, string value, StringComparison comparisonType)
        {
            if(str == null)
                throw new ArgumentNullException(nameof(str));
            return str.IndexOf(value, comparisonType) != -1;
        }
        public static string Replace(this string str, string oldValue, string newValue, StringComparison comparisonType)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));

            if ( comparisonType == StringComparison.Ordinal)
            {
                return str.Replace(oldValue, newValue);
            }

            int position;
            while ((position = str.IndexOf(oldValue, comparisonType)) > -1)
            {
                str = str.Remove(position, oldValue.Length);
                str = str.Insert(position, newValue);
            }
            return str;
        }
#endif
    }
}
