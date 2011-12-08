﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Routing;
using System.Web;
using System.IO;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace NetBash.UI
{
    public class NetBashHandler : IRouteHandler, IHttpHandler
    {
        internal static HtmlString RenderIncludes()
        {
            const string format =
@"<link rel=""stylesheet"" type=""text/css"" href=""{0}netbash-includes.css?v={1}"">
<script type=""text/javascript"">
    if (!window.jQuery) document.write(unescape(""%3Cscript src='{0}netbash-jquery.js' type='text/javascript'%3E%3C/script%3E""));
</script>
<script type=""text/javascript"" src=""{0}netbash-includes.js?v={1}""></script>";

            var result = "";
            result = string.Format(format, ensureTrailingSlash(VirtualPathUtility.ToAbsolute(NetBash.Settings.RouteBasePath)), NetBash.Settings.Version); 

            return new HtmlString(result);
        }

        internal static void RegisterRoutes()
        {
            var urls = new[] 
            {  
                "netbash",
                "netbash-jquery.js",
                "netbash-includes.js",
                "netbash-includes.css"
            };

            var routes = RouteTable.Routes;
            var handler = new NetBashHandler();
            var prefix = ensureTrailingSlash((NetBash.Settings.RouteBasePath ?? "").Replace("~/", ""));

            using (routes.GetWriteLock())
            {
                foreach (var url in urls)
                {
                    var route = new Route(prefix + url, handler)
                    {
                        // we have to specify these, so no MVC route helpers will match, e.g. @Html.ActionLink("Home", "Index", "Home")
                        Defaults = new RouteValueDictionary(new { controller = "NetBashHandler", action = "ProcessRequest" })
                    };

                    // put our routes at the beginning, like a boss
                    routes.Insert(0, route);
                }
            }
        }

        private static string ensureTrailingSlash(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return Regex.Replace(input, "/+$", "") + "/";
        }

        /// <summary>
        /// Returns this <see cref="MiniProfilerHandler"/> to handle <paramref name="requestContext"/>.
        /// </summary>
        public IHttpHandler GetHttpHandler(RequestContext requestContext)
        {
            return this; // elegant? I THINK SO.
        }

        /// <summary>
        /// Try to keep everything static so we can easily be reused.
        /// </summary>
        public bool IsReusable
        {
            get { return true; }
        }

        /// <summary>
        /// Returns either includes' css/javascript or results' html.
        /// </summary>
        public void ProcessRequest(HttpContext context)
        {
            string output;
            string path = context.Request.AppRelativeCurrentExecutionFilePath;

            switch (Path.GetFileNameWithoutExtension(path).ToLower())
            {
                case "netbash-jquery":
                case "netbash-includes":
                    output = Includes(context, path);
                    break;

                case "netbash":
                    output = RenderCommand(context);
                    break;

                default:
                    output = NotFound(context);
                    break;
            }

            context.Response.Write(output);
        }

        private static string RenderCommand(HttpContext context)
        {
            var commandResponse = "";
            var success = true;

            try
            {
                commandResponse = NetBash.Current.Process(context.Request.Params["Command"]);
            }
            catch (Exception argNull)
            {
                success = false;
                commandResponse = argNull.Message;
            }

            var response = new { Success = success, IsRaw = true, Content = commandResponse };

            context.Response.ContentType = "application/json";
            return JsonConvert.SerializeObject(response);
        }

        /// <summary>
        /// Handles rendering static content files.
        /// </summary>
        private static string Includes(HttpContext context, string path)
        {
            var response = context.Response;

            switch (Path.GetExtension(path))
            {
                case ".js":
                    response.ContentType = "application/javascript";
                    break;
                case ".css":
                    response.ContentType = "text/css";
                    break;
                default:
                    return NotFound(context);
            }

            var cache = response.Cache;
            cache.SetCacheability(System.Web.HttpCacheability.Public);
            cache.SetExpires(DateTime.Now.AddDays(7));
            cache.SetValidUntilExpires(true);

            var embeddedFile = Path.GetFileName(path).Replace("netbash-", "");
            return GetResource(embeddedFile);
        }

        private static string GetResource(string filename)
        {
            string result;

            if (!_ResourceCache.TryGetValue(filename, out result))
            {
                using (var stream = typeof(NetBashHandler).Assembly.GetManifestResourceStream("NetBash.UI." + filename))
                using (var reader = new StreamReader(stream))
                {
                    result = reader.ReadToEnd();
                }

                _ResourceCache[filename] = result;
            }

            return result;
        }

        /// <summary>
        /// Embedded resource contents keyed by filename.
        /// </summary>
        private static readonly Dictionary<string, string> _ResourceCache = new Dictionary<string, string>();

        /// <summary>
        /// Helper method that sets a proper 404 response code.
        /// </summary>
        private static string NotFound(HttpContext context, string contentType = "text/plain", string message = null)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = contentType;

            return message;
        }
    }
}