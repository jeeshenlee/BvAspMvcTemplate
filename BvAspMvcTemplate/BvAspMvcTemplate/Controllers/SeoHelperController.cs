using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;
using System.Xml;
using System.Xml.Linq;

namespace BvAspMvcTemplate.Controllers
{
    public class SeoHelperController : Controller
    {
        [Route("robots.txt")]
        public FileContentResult GenerateRobot()
        {
            StringBuilder stringBuilder = new StringBuilder();

            // Set robots.txt rules here
            stringBuilder.AppendLine("user-agent: *");
            stringBuilder.AppendLine("disallow: /error/");
            stringBuilder.AppendLine("disallow: /Administration/");
            stringBuilder.AppendLine("disallow: /Admin/");
            stringBuilder.AppendLine("disallow: /Account/");

            if (Request.Url != null)
            {
                stringBuilder.Append("sitemap: ");
                stringBuilder.AppendLine(Request.Url.GetLeftPart(UriPartial.Authority) + "/sitemap.xml");
            }

            return File(Encoding.UTF8.GetBytes(stringBuilder.ToString()), "text/plain");
        }

        [Route("sitemap.xml")]
        public XmlSitemapResult GenerateSiteMap()
        {
            string sitePrimaryUrl = System.Web.HttpContext.Current.Request.Url.OriginalString;

            if (System.Web.HttpContext.Current.Request.Url.PathAndQuery != "/")
                sitePrimaryUrl = sitePrimaryUrl.Replace(System.Web.HttpContext.Current.Request.Url.PathAndQuery, "");

            UriBuilder uri = new UriBuilder(sitePrimaryUrl);
            Crawl c1 = new Crawl();
            c1.PrimaryUrl = sitePrimaryUrl;
            c1.PrimaryHost = uri.Host;
            c1.GetUrlsOfSite(sitePrimaryUrl);

            foreach (var pageUrl in Crawl.PageUrls)
            {
                var locationResult = new LocationUrls_Result()
                {
                    Url = pageUrl.ToLowerInvariant(),
                    ImageUrls = c1.GetImagesUrlOfSite(pageUrl),
                };
                Crawl.Urls.Add(locationResult);
            }

            List<LocationUrls_Result> lstSItemapResult = new List<LocationUrls_Result>();
            foreach (var url in Crawl.Urls)
            {
                lstSItemapResult.Add(new LocationUrls_Result() { Url = url.Url, ImageUrls = url.ImageUrls, Changefreq = "monthly", LastModified = DateTime.UtcNow});
            }

            return new XmlSitemapResult(lstSItemapResult);
        }
    }

    public class XmlSitemapResult : ActionResult
    {
        private IEnumerable<LocationUrls_Result> _items;

        public XmlSitemapResult(IEnumerable<LocationUrls_Result> items)
        {
            _items = items;
        }

        public override void ExecuteResult(ControllerContext context)
        {
            string encoding = context.HttpContext.Response.ContentEncoding.WebName;
            XNamespace _xmlns = "http://www.sitemaps.org/schemas/sitemap/0.9";

            XDocument sitemap = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement(_xmlns + "urlset",
                    new XAttribute(XNamespace.Xmlns + "image", "http://www.google.com/schemas/sitemap-image/1.1"),
                    new XAttribute(XNamespace.Xmlns + "mobile", "http://www.google.com/schemas/sitemap-mobile/1.0"),
                    from item in _items
                    select CreateItemElement(item)
                    )
                );

            context.HttpContext.Response.ContentType = "application/rss+xml";
            context.HttpContext.Response.Flush();
            context.HttpContext.Response.Write(sitemap.Declaration + sitemap.ToString());
        }

        private XElement CreateItemElement(LocationUrls_Result item)
        {
            XNamespace _xmlns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            XNamespace imageNs = "http://www.google.com/schemas/sitemap-image/1.1";
            XNamespace mobileNs = "http://www.google.com/schemas/sitemap-mobile/1.0";

            XElement itemElement = new XElement(_xmlns + "url");
            itemElement.Add(new XElement(_xmlns + "loc", item.Url.ToLower()));

            XElement imageElement = new XElement(imageNs + "image");
            foreach (var imageUrl in item.ImageUrls)
            {
                imageElement.Add(new XElement(imageNs + "loc", imageUrl.ToLower()));
            }
            itemElement.Add(imageElement);

            if (item.LastModified.HasValue)
                itemElement.Add(new XElement(_xmlns + "lastmod", item.LastModified.Value.ToString("yyyy-MM-dd")));

            if (item.Changefreq != null)
                itemElement.Add(new XElement(_xmlns + "changefreq", item.Changefreq.ToString().ToLower()));

            if (item.Priority.HasValue)
                itemElement.Add(new XElement(_xmlns + "priority", item.Priority.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            
            itemElement.Add(new XElement(mobileNs + "mobile"));

            return itemElement;
        }
    }

    public class Crawl
    {
        public static List<LocationUrls_Result> Urls = null;
        public static List<string> PageUrls = null; 

        public string PrimaryUrl { get; set; }
        public string PrimaryHost { get; set; }
        string CurrentUrl { get; set; }

        public Crawl()
        {
            Urls = new List<LocationUrls_Result>();
            PageUrls = new List<string>();
            CurrentUrl = System.Web.HttpContext.Current.Request.Url.OriginalString;
        }

        public void GetUrlsOfSite(string url)
        {
            if (HttpContext.Current.IsDebuggingEnabled)
                ServicePointManager.ServerCertificateValidationCallback = new System.Net.Security.RemoteCertificateValidationCallback(AcceptAllCertifications);

            WebRequest webRequest = WebRequest.Create(url);
            if (webRequest != null)
            {
                WebResponse webResponse = webRequest.GetResponse();
                Stream data = webResponse.GetResponseStream();
                string html = String.Empty;
                using (StreamReader sr = new StreamReader(data))
                {
                    html = sr.ReadToEnd();

                    Match m;
                    string HRefPattern = "<a\\s+(?:[^>]*?\\s+)?href=\"([^\"]*)\"";

                    try
                    {
                        m = Regex.Match(html, HRefPattern, RegexOptions.IgnoreCase);
                        while (m.Success)
                        {
                            string urlValue = m.Groups[1].Value;
                            if (urlValue.Trim().Length > 0 && !urlValue.Trim().StartsWith("#"))
                            {
                                string tempStr = urlValue;

                                if (tempStr.Contains("tel:") || tempStr.Contains("mailto:") ||
                                    tempStr.Contains("fax:") || IsMediaExtension(tempStr))
                                    tempStr = "";

                                if (tempStr.StartsWith(".."))
                                    tempStr = tempStr.Replace("..", "").Trim();

                                if (!urlValue.Contains("http://") && !urlValue.Contains("https://"))
                                    tempStr = PrimaryUrl + tempStr;
                                
                                if (tempStr != CurrentUrl)
                                {
                                    UriBuilder uri = new UriBuilder(tempStr);
                                    if (uri.Host == PrimaryHost)
                                    {
                                        if (!PageUrls.Contains(uri.Uri.ToString().ToLowerInvariant()))
                                        {
                                            PageUrls.Add(uri.Uri.ToString().ToLowerInvariant());
                                            GetUrlsOfSite(uri.Uri.ToString());
                                        }
                                    }
                                }
                            }
                            m = m.NextMatch();
                        }
                    }
                    catch (Exception ex)
                    {

                    }
                }
            }
        }

        public List<string> GetImagesUrlOfSite(string url)
        {
            var imagesUrl = new List<string>();

            if (HttpContext.Current.IsDebuggingEnabled)
                ServicePointManager.ServerCertificateValidationCallback = new System.Net.Security.RemoteCertificateValidationCallback(AcceptAllCertifications);

            WebRequest webRequest = WebRequest.Create(url);
            if (webRequest != null)
            {
                WebResponse webResponse = webRequest.GetResponse();
                Stream data = webResponse.GetResponseStream();
                string html = String.Empty;
                using (StreamReader sr = new StreamReader(data))
                {
                    html = sr.ReadToEnd();

                    Match m;
                    string HRefPattern = "<img\\s+(?:[^>]*?\\s+)?src=\"([^\"]*)\"";

                    try
                    {
                        m = Regex.Match(html, HRefPattern, RegexOptions.IgnoreCase);
                        while (m.Success)
                        {
                            string urlValue = m.Groups[1].Value;
                            if (urlValue.Trim().Length > 0 && !urlValue.Trim().StartsWith("#"))
                            {
                                string tempStr = urlValue;

                                if (tempStr.StartsWith(".."))
                                    tempStr = tempStr.Replace("..", "").Trim();

                                if (!urlValue.Contains("http://") && !urlValue.Contains("https://"))
                                    tempStr = PrimaryUrl + tempStr;

                                if (tempStr != CurrentUrl)
                                {
                                    UriBuilder uri = new UriBuilder(tempStr);
                                    if (uri.Host == PrimaryHost)
                                    {
                                        if (IsMediaExtension(uri.Uri.ToString().ToLowerInvariant()) && !imagesUrl.Contains(uri.Uri.ToString().ToLowerInvariant()))
                                        {
                                            imagesUrl.Add(uri.Uri.ToString().ToLowerInvariant());
                                        }
                                    }
                                }
                            }
                            m = m.NextMatch();
                        }
                    }
                    catch (Exception ex)
                    {

                    }
                }
            }
            return imagesUrl;
        }

        public bool AcceptAllCertifications(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certification, System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        public bool IsMediaExtension(string path)
        {
            string[] mediaExtensions =
            {
                ".PNG", ".JPG", ".JPEG", ".BMP", ".GIF",
                ".WAV", ".MID", ".MIDI", ".WMA", ".MP3", ".OGG", ".RMA",
                ".AVI", ".MP4", ".DIVX", ".WMV"
            };
            return mediaExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        }
    }

    public class LocationUrls_Result
    {
        public string Url { get; set; }
        public List<string> ImageUrls { get; set; } 
        public string Changefreq { get; set; }
        public DateTime? LastModified { get; set; }
        public double? Priority { get; set; }
        public int? OrderBy { get; set; }
    }
}