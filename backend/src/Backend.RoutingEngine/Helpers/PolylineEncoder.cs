using System.Text;

namespace Backend.RoutingEngine.Helpers;

public static class PolylineEncoder
{
    public static string Encode(IEnumerable<(double Latitude, double Longitude)> points)
    {
        var str = new StringBuilder();

        void EncodeDiff(int diff)
        {
            int val = diff < 0 ? ~(diff << 1) : (diff << 1);
            while (val >= 0x20)
            {
                str.Append((char)((0x20 | (val & 0x1f)) + 63));
                val >>= 5;
            }
            str.Append((char)(val + 63));
        }

        int lastLat = 0;
        int lastLng = 0;

        foreach (var (lat, lng) in points)
        {
            int currentLat = (int)Math.Round(lat * 1e5);
            int currentLng = (int)Math.Round(lng * 1e5);

            EncodeDiff(currentLat - lastLat);
            EncodeDiff(currentLng - lastLng);

            lastLat = currentLat;
            lastLng = currentLng;
        }

        return str.ToString();
    }
}
