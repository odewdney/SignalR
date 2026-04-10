// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
#if !NETCOREAPP
using Microsoft.Owin;
#endif

namespace Microsoft.AspNet.SignalR.Hosting
{
    /// <summary>
    /// Extension methods for <see cref="IResponse"/>.
    /// </summary>
    public static class ResponseExtensions
    {
        /// <summary>
        /// Closes the connection to a client with optional data.
        /// </summary>
        /// <param name="response">The <see cref="IResponse"/>.</param>
        /// <param name="data">The data to write to the connection.</param>
        /// <returns>A task that represents when the connection is closed.</returns>
        public static async Task End(this IResponse response, string data)
        {
            if (response == null)
            {
                throw new ArgumentNullException("response");
            }

            var bytes = Encoding.UTF8.GetBytes(data);
            await response.WriteAsync(new ArraySegment<byte>(bytes, 0, bytes.Length));
        }
    }
}
