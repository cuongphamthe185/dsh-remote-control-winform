using System.Text.RegularExpressions;

namespace DshRemote
{
    /// <summary>
    /// Hides credentials before text reaches the screen or a log file.
    /// A DSH pairing URL (?token=...) grants remote control of this PC, so it
    /// must never be shown in the Activity console, a dialog or gui.log.
    /// </summary>
    public static class SecretMask
    {
        public const string Placeholder = "***";

        // Query/fragment parameters that carry credentials, e.g.
        //   http://100.x.x.x:8443/?token=abc  ->  http://100.x.x.x:8443/?token=***
        private static readonly Regex QueryParam = new Regex(
            @"([?&#;](?:token|access_token|auth|auth_token|key|api_key|apikey|secret|password|session|sig|signature)=)[^\s&#""'<>]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string Apply(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            return QueryParam.Replace(text, "$1" + Placeholder);
        }
    }
}
