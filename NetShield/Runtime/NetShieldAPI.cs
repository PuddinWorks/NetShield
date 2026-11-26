using System.Threading.Tasks;

namespace NetShield
{
    public static class NetShieldAPI
    {
        public static bool IsAvailable => true;
        public static NetShieldResult CheckIP(string ip, NetShieldSettings settings = null)
        {
            return NetShieldDatabase.Shared.Check(ip, settings ?? new NetShieldSettings());
        }
        public static async Task<NetShieldResult> CheckIPAsync(string ip, NetShieldSettings settings = null)
        {
            var options = settings ?? new NetShieldSettings();
            if (options.enabled && options.autoUpdate && !options.offline)
                await NetShieldDatabase.Shared.RefreshAsync(options);
            return CheckIP(ip, options);
        }
    }
}
