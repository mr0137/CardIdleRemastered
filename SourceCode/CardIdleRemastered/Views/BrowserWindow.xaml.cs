﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace CardIdleRemastered
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class BrowserWindow : Window
    {
        private const string EmulationKey = @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION";
        private static bool _browserEmulation;
        public CookieCollection CookieCollection { get; private set; }

        public BrowserWindow()
        {
            InitializeComponent();
            SetIeMode();
        }

        public ISettingsStorage Storage { get; set; }

        /// <summary>
        /// Set the Browser emulation version for embedded browser control (registry key) to use newest IE version
        /// </summary>
        private void SetIeMode()
        {
            if (_browserEmulation)
                return;

            try
            {
                var ieVersion = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Internet Explorer").GetValue("Version").ToString();
                Logger.Info("IE version " + ieVersion);
                RegistryKey ie_root = Registry.CurrentUser.CreateSubKey(EmulationKey);
                RegistryKey key = Registry.CurrentUser.OpenSubKey(EmulationKey, true);

                var emulationVersion = ieVersion.StartsWith("9.11") ? 11000 : 10001;
                key.SetValue(App.AppSystemName, emulationVersion, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Registry IE FEATURE_BROWSER_EMULATION");
            }
            finally
            {
                _browserEmulation = true;
            }
        }

        private void OnCookieLoaded()
        {
            GetSessionCookies(CookieCollection);

            var url = wbAuth.Source.AbsoluteUri;

            if (false == String.IsNullOrWhiteSpace(Storage.SteamLoginSecure))
            {
                if (App.CardIdle.IsNewUser)
                {
                    App.CardIdle.IsNewUser = false;
                    Title = Properties.Resources.TradingCardsFAQ;
                    wbAuth.Source = (new Uri("https://steamcommunity.com/tradingcards/faq"));
                }
                else if (url.StartsWith(@"https://steamcommunity.com/id/"))
                {
                    Close();
                }
            }
        }

        #region Dll Imports

        [DllImport("wininet.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool InternetSetOption(int hInternet, int dwOption, string lpBuffer, int dwBufferLength);

        [DllImport("wininet.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool InternetSetCookie(string lpszUrlName, string lpszCookieName, string lpszCookieData);

        // Imports the InternetGetCookieEx function from wininet.dll which allows the application to access the cookie data from the web browser control
        // Reference: http://stackoverflow.com/questions/3382498/is-it-possible-to-transfer-authentication-from-webbrowser-to-webrequest
        [DllImport("wininet.dll", SetLastError = true)]
        public static extern bool InternetGetCookieEx(string url, string cookieName, StringBuilder cookieData, ref int size, int dwFlags, IntPtr lpReserved);

        #endregion

        private void BrowserWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Remove any existing session state data
            InternetSetOption(0, 42, null, 0);

            // Delete Steam cookie data from the browser control
            InternetSetCookie("https://steamcommunity.com", "sessionid", ";expires=Mon, 01 Jan 0001 00:00:00 GMT");
            InternetSetCookie("https://steamcommunity.com", "steamLogin", ";expires=Mon, 01 Jan 0001 00:00:00 GMT");
            InternetSetCookie("https://steamcommunity.com", "steamLoginSecure", ";expires=Mon, 01 Jan 0001 00:00:00 GMT");
            InternetSetCookie("https://steamcommunity.com", "steamRememberLogin", ";expires=Mon, 01 Jan 0001 00:00:00 GMT");
        }

        // Assigns the hex value for the DLL flag that allows Idle Master to be able to access cookie data marked as "HTTP Only"
        private const int InternetCookieHttpOnly = 0x2000;

        private async Task GatherCookies(string url)
        {
            //access the cookies  
            List<CoreWebView2Cookie> wv2Cookies = new List<CoreWebView2Cookie>();
            if (wbAuth.CoreWebView2 != null)
            {
                wv2Cookies = await wbAuth.CoreWebView2.CookieManager.GetCookiesAsync("https://steamcommunity.com");
            }
            CookieCollection sysNetCookieCollection = new CookieCollection();
            wv2Cookies.ForEach(c => sysNetCookieCollection.Add(c.ToSystemNetCookie()));

            //set the cookies in the CookieCollection property  
            CookieCollection = sysNetCookieCollection;

            OnCookieLoaded();
        }


        /// <summary>
        /// Returns cookie data based on Uri
        /// </summary>
        /// <param name="uri"></param>
        /// <returns></returns>
        public CookieContainer GetUriCookieContainer(Uri uri)
        {
            // First, create a null cookie container
            CookieContainer cookies = null;

            Application.Current.Dispatcher.Invoke(() => GatherCookies(uri.ToString()));

            // Determine cookie size
            int datasize = 8192 * 16;

            var cookieData = new StringBuilder(datasize);

            // Call InternetGetCookieEx from wininet.dll
            if (false == InternetGetCookieEx(uri.ToString(), null, cookieData, ref datasize, InternetCookieHttpOnly, IntPtr.Zero))
            {
                if (datasize < 0)
                    return null;

                // Allocate stringbuilder large enough to hold the cookie
                cookieData = new StringBuilder(datasize);
                if (false == InternetGetCookieEx(
                    uri.ToString(),
                    null, cookieData,
                    ref datasize,
                    InternetCookieHttpOnly,
                    IntPtr.Zero))
                    return null;
            }

            // If the cookie contains data, add it to the cookie container
            if (cookieData.Length > 0)
            {
                cookies = new CookieContainer();
                cookies.SetCookies(uri, cookieData.ToString().Replace(';', ','));
            }

            // Return the cookie container
            return cookies;
        }

        private void BrowserNavigated(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            // Get the URL of the page that just finished loading
            var src = wbAuth.Source;
            var url = wbAuth.Source.AbsoluteUri;

            Logger.Info("Navigated to " + url);

            // If the page it just finished loading is the login page
            if (url == "https://steamcommunity.com/login/home/?goto=my/profile" ||
                url == "https://store.steampowered.com/login/transfer" ||
                url == "https://store.steampowered.com//login/transfer")
            {
                // Get a list of cookies from the current page                
                var cookies = GetUriCookieContainer(src).GetCookies(src);
                foreach (Cookie cookie in cookies)
                {
                    if (cookie.Name.StartsWith("steamMachineAuth"))
                        Storage.MachineAuth = cookie.Value;
                }
            }
            // If the page it just finished loading isn't the login page
            else if (url.StartsWith("javascript:") == false && url.StartsWith("about:") == false)
            {
                Application.Current.Dispatcher.Invoke(() => { GatherCookies(url.ToString()); });
            }
        }

        /// <summary>
        /// Extract session auth cookies
        /// </summary>
        /// <param name="cookies"></param>
        private void GetSessionCookies(CookieCollection cookies)
        {
            foreach (Cookie cookie in cookies)
            {
                if (cookie.Name == "sessionid")
                {
                    Storage.SessionId = cookie.Value;
                }

                // Save the "steamLogin" cookie and construct and save the user's profile link
                else if (cookie.Name == "steamLoginSecure")
                {
                    string login = cookie.Value;
                    Storage.SteamLoginSecure = login;

                    var steamId = WebUtility.UrlDecode(login);
                    var index = steamId.IndexOf('|');
                    if (index >= 0)
                        steamId = steamId.Remove(index);
                    Storage.SteamProfileUrl = "https://steamcommunity.com/profiles/" + steamId;
                }
                else if (cookie.Name == "steamparental")
                {
                    Storage.SteamParental = cookie.Value;
                }
                else if (cookie.Name == "steamRememberLogin")
                {
                    Storage.SteamRememberLogin = cookie.Value;
                }
            }
        }
    }
}
