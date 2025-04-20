using System.Globalization;
using System.Text;

namespace AstroTool;

internal class Program
{
    private static void Main(string[] args)
    {
        if ( args.Length == 0 )
        {
            Console.WriteLine("Send View Post command to Stellarium with the RA/DEC Value from a Fits file");
            Console.WriteLine("Commandline Parameter: Path to Fits File");
            return;
        }

        string filename = args[0];
        FileInfo fileInfo = new(filename);
        if ( !fileInfo.Exists )
        {
            Console.WriteLine($"File {filename} does not exist.");
            return;
        }

        Console.WriteLine($"Reading {fileInfo.FullName}");
        List<string> d = ReadFitsFiles(fileInfo.FullName);
        FitsData fd = new(d);
        fd.Debug();
        /*
         *
         * M13
         * RA      =   250.44218951953331 / Epoch : J2000
         * DEC     =    36.45730116119505 / Epoch : J2000
         * 04/05/2025 23:21:23
         */
        (string value, string comment)? ra = fd.GetValue("ra");
        (string value, string comment)? dec = fd.GetValue("dec");
        if ( ra == null )
        {
            Console.WriteLine("No RA value found in fits file");
            return;
        }

        if ( dec == null )
        {
            Console.WriteLine("No DEC value found in fits file");
            return;
        }

        double.TryParse(ra.Value.value.Trim(), CultureInfo.InvariantCulture, out double ra_val);
        double.TryParse(dec.Value.value.Trim(), CultureInfo.InvariantCulture, out double dec_val);
        double ra_rad = Radians(ra_val);
        double dec_rad = Radians(dec_val);
        double x = Math.Cos(dec_rad) * Math.Cos(ra_rad);
        double y = Math.Cos(dec_rad) * Math.Sin(ra_rad);
        double z = Math.Sin(dec_rad);

        // Send post request to the server endpoint http://localhost:8090/api/main/view
        // example in curl
        // curl -d "j2000=[-0.269,-0.757, 0.594]" http://localhost:8090/api/main/view
        HttpClient client = new();
        string url = "http://localhost:8090/api/main/view";
        Dictionary<string, string> data = new()
        {
            { "j2000", $"[{x.ToString(CultureInfo.InvariantCulture)},{y.ToString(CultureInfo.InvariantCulture)},{z.ToString(CultureInfo.InvariantCulture)}]" }
        };
        FormUrlEncodedContent content = new(data);
        Console.WriteLine($"Sending Stellarium to {data.Values.First()}");
        HttpResponseMessage response = client.PostAsync(url, content).Result;
        if ( response.IsSuccessStatusCode )
        {
            string responseString = response.Content.ReadAsStringAsync().Result;
            Console.WriteLine(responseString);
        }
        else
        {
            Console.WriteLine($"Error: {response.StatusCode}");
        }

        Thread.Sleep(2000);
    }

    public static double Radians(double v)
    {
        return v * (Math.PI / 180.0);
    }

    public static List<string> ReadFitsFiles(string filename)
    {
        //read first 2880 bytes of file / HDU = immer 2880 bytes
        List<string> fitsdata = new();
        using (FileStream fileStream = new(filename, FileMode.Open, FileAccess.Read))
        {
            int maxBlocks = 10;
            int bytesRead = 0;
            byte[] buffer = new byte[2880];
            while (maxBlocks > 0)
            {
                maxBlocks--;
                bytesRead = fileStream.Read(buffer, 0, buffer.Length);
                for (int i = 0; i < bytesRead; i += 80)
                {
                    string s = Encoding.ASCII.GetString(buffer, i, 80);
                    fitsdata.Add(s);
                    if ( s.StartsWith("END ", StringComparison.OrdinalIgnoreCase) )
                    {
                        maxBlocks = 0;
                        break;
                    }
                }
            }
        }

        return fitsdata;
    }

    public class FitsData
    {
        private readonly List<string> _fitsdata;

        public FitsData(List<string> d)
        {
            _fitsdata = d;
        }

        public void Debug()
        {
            Console.WriteLine($"NAXIS1 {GetValue("NAXIS1")?.value}");
            Console.WriteLine($"NAXIS2 {GetValue("NAXIS2")?.value}");
            Console.WriteLine($"INSTRUME {GetValue("INSTRUME")?.value}");
            Console.WriteLine($"RA {GetValue("RA")?.value}");
            Console.WriteLine($"DEC {GetValue("DEC")?.value}");
        }

        public string? FindValue(string key)
        {
            string search = key.PadRight(8, ' ') + "=";
            string? found = _fitsdata.FirstOrDefault(i => i.StartsWith(search, StringComparison.InvariantCultureIgnoreCase));
            if ( found != null )
            {
                return found.Substring(10);
            }

            return null;
        }

        public (string value, string comment)? GetValue(string key)
        {
            string? s = FindValue(key);
            if ( s == null )
            {
                return null;
            }

            return ParseValue(s);
        }

        public (string, string) ParseValue(string s)
        {
            StringBuilder sbValue = new();
            StringBuilder sbComment = new();
            bool inStr = false;
            StringBuilder target = sbValue;
            foreach (char c in s)
            {
                if ( c == '\'' )
                {
                    inStr = !inStr;
                }

                if ( c == '/' && !inStr )
                {
                    target = sbComment;
                    continue;
                }

                target.Append(c);
            }

            return (sbValue.ToString(), sbComment.ToString());
        }
    }
}