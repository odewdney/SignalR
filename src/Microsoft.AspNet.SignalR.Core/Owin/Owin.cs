using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

#if NETCOREAPP
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;

namespace Microsoft.AspNet.SignalR.Owin
{
    using AppFunc = Func<IDictionary<string, object>, Task>;

    public interface IOwinRequest
    {
        Uri Uri { get; }
        string Method { get; }
        string Scheme { get;  }
        bool IsSecure { get; }
        HostString Host { get; }
        PathString Path { get; set; }
        PathString PathBase { get; set; }
        IReadableStringCollection Query { get; }
        string Protocol { get; }
        IHeaderDictionary Headers { get; }
        IPrincipal User { get; }

        IRequestCookieCollection Cookies { get; }

        Task<IFormCollection> ReadFormAsync();
    }
    public interface IOwinResponse
    {
        int StatusCode { get; set; }
        string ReasonPhrase { get; set; }

        string ContentType {  get; set; }
        Stream Body { get; }
    }

    public interface IOwinContext
    {
        IDictionary<string, object> Environment { get; }
        IOwinRequest Request { get; }
        IOwinResponse Response { get; }

    }

    public interface IReadableStringCollection : IEnumerable<KeyValuePair<string, string[]>>
    {
#pragma warning disable CA1716 // Identifiers should not match keywords
        string Get(string key);
#pragma warning restore CA1716 // Identifiers should not match keywords
        string this[string key] { get; }
        IList<string> GetValues(string key);
    }

    public interface IFormCollection : IReadableStringCollection
    { }

    public class ReadableStringCollection : IReadableStringCollection
    {
        public ReadableStringCollection(IDictionary<string, string[]> qs)
        {
            _qs = qs;
        }

        readonly IDictionary<string, string[]> _qs;

        public string this[string key] => Get(key);

        public string Get(string key)
        {
            if (_qs?.TryGetValue(key, out var val) == true && val != null)
            {
                return string.Join(',', val);
            }
            return null;
        }

        public IList<string> GetValues(string key) => _qs[key];

        public IEnumerator<KeyValuePair<string, string[]>> GetEnumerator() => _qs.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _qs.GetEnumerator();
    }

    public interface IHeaderDictionary : IReadableStringCollection, IDictionary<string, string[]>
    {
#pragma warning disable CA1716 // Identifiers should not match keywords
        void Set(string key, string value);
#pragma warning restore CA1716 // Identifiers should not match keywords
    }

    public class FormCollection : ReadableStringCollection, IFormCollection
    {
        public FormCollection(IDictionary<string, string[]> qs)
            : base(qs)
        { }
    }

    public class HeaderDictionary : IHeaderDictionary
    {
        private readonly IDictionary<string, string[]> store;

        public HeaderDictionary(IDictionary<string, string[]> store)
        {
            this.store = store;
        }

        public string this[string key] => Get(key);

        string[] IDictionary<string, string[]>.this[string key] { get => store[key]; set => store[key] = value; }

        public ICollection<string> Keys => store.Keys;

#pragma warning disable CA1721 // Property names should not match get methods
        public ICollection<string[]> Values => store.Values;
#pragma warning restore CA1721 // Property names should not match get methods

        public int Count => store.Count;

        public bool IsReadOnly => store.IsReadOnly;

        public void Add(string key, string[] value) => store.Add(key, value);

        public void Add(KeyValuePair<string, string[]> item) => store.Add(item);
        public void Clear() => store.Clear();

        public bool Contains(KeyValuePair<string, string[]> item) => store.Contains(item);

        public bool ContainsKey(string key) => store.ContainsKey(key);

        public void CopyTo(KeyValuePair<string, string[]>[] array, int arrayIndex) => store.CopyTo(array, arrayIndex);

        public string Get(string key) => store.TryGetValue(key, out var val) ? string.Join(',', val) : null;

        public IEnumerator<KeyValuePair<string, string[]>> GetEnumerator() => store.GetEnumerator();

        public bool Remove(string key) => store.Remove(key);

        public bool Remove(KeyValuePair<string, string[]> item) => store.Remove(item);

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out string[] value) => store.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator() => store.GetEnumerator();

        public void Set(string key, string value) { store[key] = [value]; }
        public IList<string> GetValues(string key) => store[key];
    }

    class RequestCookieCollection : IRequestCookieCollection
    {
        private readonly IDictionary<string, string> cookies;

        public RequestCookieCollection(IDictionary<string,string> cookies)
        {
            this.cookies = cookies;
        }

        public string this[string key] => cookies[key];
        public int Count => cookies.Count;
        public ICollection<string> Keys => cookies.Keys;
        public bool ContainsKey(string key) => cookies.ContainsKey(key);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => cookies.GetEnumerator();
        public bool TryGetValue(string key, [NotNullWhen(true)] out string value) => cookies.TryGetValue(key, out value);
        IEnumerator IEnumerable.GetEnumerator() => cookies.GetEnumerator();
    }

    public class OwinRequest : IOwinRequest
    {
        public OwinRequest(IDictionary<string, object> env)
        {
            Environment = env;
            var qs = Environment[OwinConstants.RequestQueryString] as string;
            var qsd = QueryHelpers.ParseNullableQuery(qs);
            _qs = new ReadableStringCollection(qsd?.ToDictionary(p1=>p1.Key, p2=>p2.Value.ToArray()));
            var ub = new UriBuilder(Scheme, Host.Host, Host.Port ?? 80, Path);
            uri = ub.Uri;
        }

        readonly Uri uri;
        readonly IReadableStringCollection _qs;
        public IDictionary<string, object> Environment { get; private set; }

        public IReadableStringCollection Query => _qs;

        public IPrincipal User => null;

        public string Method => Get<string>(OwinConstants.RequestMethod);
        public string Scheme => Get<string>(OwinConstants.RequestScheme);
        public bool IsSecure => string.Equals(Scheme, "https", StringComparison.OrdinalIgnoreCase);
        public HostString Host => new HostString(GetHeader("host")); 
        public PathString PathBase { get => Get<string>(OwinConstants.RequestPathBase); set => Set(OwinConstants.RequestPathBase, value); }
        public PathString Path { get => Get<string>(OwinConstants.RequestPath); set => Set(OwinConstants.RequestPath, value); }


        public Uri Uri => uri;
        public string Protocol => Get<string>(OwinConstants.RequestProtocol);

        public IHeaderDictionary Headers => new HeaderDictionary(RawHeaders);//.ToDictionary(p1 => p1.Key, p2 => new StringValues(p2.Value)));

        public string ContentType { get => GetHeader(Microsoft.Net.Http.Headers.HeaderNames.ContentType); }
        public string CacheControl { get => GetHeader(Microsoft.Net.Http.Headers.HeaderNames.CacheControl); }

        public virtual CancellationToken CallCancelled { get => Get<CancellationToken>(OwinConstants.CallCancelled); }

#pragma warning disable CA1716 // Identifiers should not match keywords
        public virtual T Get<T>(string key) => Environment.TryGetValue(key, out var value) ? (T)value : default;
#pragma warning restore CA1716 // Identifiers should not match keywords
        void Set<T>(string key, T Value) => Environment[key] = Value;

        private IDictionary<string, string[]> RawHeaders { get => Get<IDictionary<string, string[]>>(OwinConstants.RequestHeaders); }

        public IRequestCookieCollection Cookies => new RequestCookieCollection(new Dictionary<string,string>());

        string GetHeader(string key) => (RawHeaders.TryGetValue(key, out var value)) ? string.Join(',', value)  : null;

        public Stream Body { get => Get<Stream>(OwinConstants.RequestBody); }

        public async Task<IFormCollection> ReadFormAsync()
        {
            var form = Get<IFormCollection>("Microsoft.Owin.Form#collection");
            if (form == null)
            {
                string text;
                // Don't close, it prevents re-winding.
                using (var reader = new StreamReader(Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4 * 1024, leaveOpen: true))
                {
                    text = await reader.ReadToEndAsync();
                }
                var qs = HttpUtility.ParseQueryString(text);
                var f = new Dictionary<string, string[]>();
                foreach (var k in qs.AllKeys)
                {
                    var v = qs.GetValues(k);
                    f.Add(k, v);
                }
                form = new FormCollection(f);
                Set("Microsoft.Owin.Form#collection", form);
            }

            return form;

        }
    }

    public class OwinResponse : IOwinResponse
    {
        public OwinResponse(IDictionary<string, object> env)
        {
            _env = env;
        }

        readonly IDictionary<string, object> _env;

        public Stream Body { get => Get<Stream>(OwinConstants.ResponseBody); }

        public int StatusCode { get => Get<int>(OwinConstants.ResponseStatusCode, 200); set => _env[OwinConstants.ResponseStatusCode]=value; }
        public string ReasonPhrase { get => Get<string>(OwinConstants.ResponseReasonPhrase); set => _env[OwinConstants.ResponseReasonPhrase] = value; }

        public IHeaderDictionary Headers => new HeaderDictionary(RawHeaders);//.ToDictionary(p1 => p1.Key, p2 => new StringValues(p2.Value)));

        public string ContentType { get => GetHeader(Microsoft.Net.Http.Headers.HeaderNames.ContentType); set => SetHeader(Microsoft.Net.Http.Headers.HeaderNames.ContentType, value); }

        public virtual CancellationToken CallCancelled { get => Get<CancellationToken>(OwinConstants.CallCancelled); }

        public T Get<T>(string key, T fallback = default) => _env.TryGetValue(key, out var val) ? (T)val : fallback;
        private IDictionary<string, string[]> RawHeaders { get => Get<IDictionary<string, string[]>>(OwinConstants.ResponseHeaders); }

        string GetHeader(string key) => (RawHeaders.TryGetValue(key, out var value)) ? string.Join(',', value) : null;
        void SetHeader(string key, string value) => RawHeaders[key] = [value];
    }

    public class OwinContext : IOwinContext
    {
        public IDictionary<string, object> Environment { get; private set; }

        public IOwinRequest Request { get; private set; }

        public IOwinResponse Response { get; private set; }

        public OwinContext(IDictionary<string, object> environment)
        {
            Environment = environment;
            Request = new OwinRequest(environment);
            Response = new OwinResponse(environment);
        }
    }

    public abstract class OwinMiddleware
    {
        protected OwinMiddleware(OwinMiddleware next) {
            Next = next;
        }

        public abstract Task Invoke(IOwinContext context);
        public OwinMiddleware Next { get; }
    }

    public enum PipelineStage
    {
        None,
        PostAuthorize,
    }

    public interface IAppBuilder
    {
        IDictionary<string, object> Properties { get; }
        IAppBuilder Use(Type t, params object[] args);

        Func<IOwinContext, Task> Build();
#pragma warning disable CA1716 // Identifiers should not match keywords
        IAppBuilder New();
#pragma warning restore CA1716 // Identifiers should not match keywords
    }

    record MapOptions(PathString PathMatch)
    { 
        public Func<IOwinContext, Task> Branch { get; set; }
    }
    class MapMiddleware : OwinMiddleware
    {
        private readonly MapOptions _options;
        public MapMiddleware(OwinMiddleware next, MapOptions options)
            :base(next)
        {
            _options = options;
        }
        public override async Task Invoke(IOwinContext context)
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments(_options.PathMatch, out var remaining))
            {
                PathString pathBase = context.Request.PathBase;
                context.Request.PathBase = pathBase + _options.PathMatch;
                context.Request.Path = remaining;
                await _options.Branch(context);
                context.Request.PathBase = pathBase;
                context.Request.Path = path;
            }
            else
            {
                await Next.Invoke(context);
            }
        }
    }

    public static class AppBuilderExt
    {
        public static IDataProtectionProvider GetDataProtectionProvider(this IAppBuilder builder) => builder?.Properties.TryGetValue("security.DataProtectionProvider", out var dp) == true && dp is IDataProtectionProvider dpp ? dpp : null;

        private const string IntegratedPipelineStageMarker = "integratedpipeline.StageMarker";

        public static void UseStageMarker(this IAppBuilder builder,PipelineStage stageMarker) => builder.UseStageMarker(stageMarker.ToString());
        public static void UseStageMarker(this IAppBuilder builder, string stageMarker)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (builder.Properties.TryGetValue(IntegratedPipelineStageMarker, out var addMarkerObj) && addMarkerObj is Action<IAppBuilder,string> addMarker)
                addMarker(builder, stageMarker);
        }

        public static IAppBuilder Map(this IAppBuilder builder, string pathMatch, Action<IAppBuilder> configuration)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.Map(new PathString(pathMatch), configuration);
        }
        public static IAppBuilder Map(this IAppBuilder builder, PathString pathMatch, Action<IAppBuilder> configuration)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configuration);
            var options = new MapOptions(pathMatch);
            var result = builder.Use<MapMiddleware>([options]);
            var builderBranch = builder.New();
            configuration(builderBranch);
            options.Branch = builderBranch.Build();
            return result;
        }

        public static IAppBuilder Use<T>(this IAppBuilder builder, params object[] args) where T : class
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.Use(typeof(T),args);  
        }
    }

    class NotFoundMiddleware : OwinMiddleware
    {
        public NotFoundMiddleware()
            : base(null)
        { }

        public override Task Invoke(IOwinContext context)
        {
            context.Response.StatusCode = 404;
            return Task.CompletedTask;
        }
    }

    public class AppBuilder : IAppBuilder
    {
        static readonly OwinMiddleware NotFound = new NotFoundMiddleware();
        record Middleware(Type t, Delegate d, object[] args);

        readonly IList<Middleware> _middleware;
        readonly IDictionary<string, object> _props;

        public AppBuilder()
        {
            _middleware = [];
            _props = new Dictionary<string, object>();
        }

        internal AppBuilder(IDictionary<string,object> props)
        {
            _middleware = [];
            _props = props;
        }

        public IDictionary<string, object> Properties => _props;

        public Func<IOwinContext, Task> Build()
        {
            var app = NotFound;

            foreach( var m in _middleware.Reverse() )
            {
                object[] args = m.args?.Length > 0 ? Enumerable.Repeat(app,1).Concat(m.args).ToArray() :new object[] { app };
                app = Activator.CreateInstance(m.t, args) as OwinMiddleware; 
            }
                
            return app.Invoke;
        }

        public Func<IDictionary<string, object>, Task> BuildAppFunc()
        {
            var f = Build();
            return d => { var ctx = new OwinContext(d); return f(ctx); };
        }

        public IAppBuilder New() => new AppBuilder(_props);

        public IAppBuilder Use(Type t, params object[] args)
        {
            _middleware.Add(new(t, null, args));
            return this;
        }
    }

    public static class SignatureConversions
    {
        public static void AddConversions(IAppBuilder app)
        {

        }
    }

    public class WSFix
    {
        private readonly RequestDelegate next;

        public WSFix(RequestDelegate next)
        {
            this.next = next;
        }

        class WSFeatureFix : IHttpWebSocketFeature
        {
            public WSFeatureFix(IHttpWebSocketFeature orig, HttpContext context)
            {
                _orig = orig;
                this.context = context;
            }

            readonly IHttpWebSocketFeature _orig;
            private readonly HttpContext context;

            public bool IsWebSocketRequest => _orig.IsWebSocketRequest;

            public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)
            {
                if (this.context.Request.Method == "CONNECT" && this.context.Response.StatusCode == 101)
                    this.context.Response.StatusCode = 200;
                return _orig.AcceptAsync(context);
            }
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context?.Request.Method == "CONNECT")
            {
                var orig = context.Features.Get<IHttpWebSocketFeature>();
                if (orig != null)
                    context.Features.Set<IHttpWebSocketFeature>(new WSFeatureFix(orig, context));
            }
            await next(context);
        }
    }

    public static class WSFixExt
    {
        public static IApplicationBuilder UseWSFix(this IApplicationBuilder app)
        {
            return app.UseMiddleware<WSFix>();
        }
    }

    class SetCapsMiddleware : OwinMiddleware
    {
        const string OwinConstants_ServerCapabilities = "server.Capabilities";
        const string OwinConstants_WebSocketVersion = "websocket.Version";
        public SetCapsMiddleware(OwinMiddleware next) : base(next) { }
        public override Task Invoke(IOwinContext context)
        {
            var cap = new Dictionary<string, object>();
            cap[OwinConstants_WebSocketVersion] = "1";
            context.Environment[OwinConstants_ServerCapabilities] = cap;
            return Next.Invoke(context);
        }
    }

}
#endif
