using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

static class Program
{
    [STAThread] // VERY IMPORTANT for Clipboard APIs
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || HasFlag(args, "--help"))
            {
                PrintHelp();
                return 0;
            }

            var cmd = args[0].ToLowerInvariant();
            return cmd switch
            {
                "get-text" => GetText(args),
                "set-text" => SetText(args),
                "get-html" => GetHtml(args),
                "set-html" => SetHtml(args),
                "get-image" => GetImage(args),
                "set-image" => SetImage(args),
                "get-files" => GetFiles(args),
                "set-files" => SetFiles(args),
                _ => Fail($"Unknown command: {cmd}")
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    static bool HasFlag(string[] a, string flag) =>
        a.Any(x => string.Equals(x, flag, StringComparison.OrdinalIgnoreCase));

    static string? GetOpt(string[] a, string name, string? def = null)
    {
        for (int i = 0; i < a.Length - 1; i++)
            if (string.Equals(a[i], name, StringComparison.OrdinalIgnoreCase))
                return a[i + 1];
        return def;
    }

    static int Fail(string message, int code = 1)
    {
        Console.Error.WriteLine(message);
        return code;
    }

    static void PrintHelp()
    {
        Console.WriteLine(
    @"ClipboardHelper (Windows)

Usage:
  ClipboardHelper get-text [--out <file>] [--no-trim]
  ClipboardHelper set-text (--text ""<value>"" | --from-file <path> | --from-stdin)

  ClipboardHelper get-html [--out <file>]
  ClipboardHelper set-html (--html ""<value>"" | --from-file <path> | --from-stdin)

  ClipboardHelper get-image [--out <file.png>] [--format png|bmp]
  ClipboardHelper set-image (--from-file <imgPath>)   // PNG/JPG/BMP/GIF supported

  ClipboardHelper get-files [--json]
  ClipboardHelper set-files (--paths ""<p1>|<p2>|..."" | --from-filelist <path>)

Notes:
  • get-* without --out prints to STDOUT (text/html) or base64 (image).
  • set-text/set-html can read from --from-stdin for large payloads.
  • get-image defaults to PNG; with no --out, outputs base64 PNG to STDOUT.
  • set-files places a FileDrop on clipboard (Explorer paste works).
");
    }


    // ---------------- TEXT ----------------
    static int GetText(string[] args)
    {
        var s = Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : "";
        if (!HasFlag(args, "--no-trim")) s = s.TrimEnd('\r', '\n');

        var outPath = GetOpt(args, "--out");
        if (!string.IsNullOrEmpty(outPath))
            File.WriteAllText(outPath, s, new UTF8Encoding(false));
        else
            Console.OutputEncoding = Encoding.UTF8; Console.Write(s);
        return 0;
    }

    static int SetText(string[] args)
    {
        string? text = GetOpt(args, "--text");
        string? fromFile = GetOpt(args, "--from-file");
        bool fromStdin = HasFlag(args, "--from-stdin");

        if (fromFile != null)
            text = File.ReadAllText(fromFile, Encoding.UTF8);
        else if (fromStdin)
            text = Console.In.ReadToEnd();

        if (text == null)
            return Fail("set-text requires --text, --from-file, or --from-stdin");

        Clipboard.SetText(text, TextDataFormat.UnicodeText);
        return 0;
    }

    // ---------------- HTML ----------------
    static int GetHtml(string[] args)
    {
        string html = Clipboard.ContainsText(TextDataFormat.Html)
            ? Clipboard.GetText(TextDataFormat.Html)
            : "";

        var outPath = GetOpt(args, "--out");
        if (!string.IsNullOrEmpty(outPath))
            File.WriteAllText(outPath, html, new UTF8Encoding(false));
        else
            Console.OutputEncoding = Encoding.UTF8; Console.Write(html);
        return 0;
    }

    static int SetHtml(string[] args)
    {
        string? html = GetOpt(args, "--html");
        string? fromFile = GetOpt(args, "--from-file");
        bool fromStdin = HasFlag(args, "--from-stdin");

        if (fromFile != null)
            html = File.ReadAllText(fromFile, Encoding.UTF8);
        else if (fromStdin)
            html = Console.In.ReadToEnd();

        if (html == null)
            return Fail("set-html requires --html, --from-file, or --from-stdin");

        // HTML clipboard format expects a specially formatted fragment.
        // If you pass raw HTML, it usually still works in most apps, but here we wrap minimal headers.
        string wrapped = WrapHtmlClipboardFormat(html);
        Clipboard.SetText(wrapped, TextDataFormat.Html);
        return 0;
    }

    // Minimal HTML Clipboard Format wrapper
    static string WrapHtmlClipboardFormat(string html)
    {
        // See "HTML Format" (CF_HTML) spec by Microsoft
        string header = "Version:0.9\r\nStartHTML:########\r\nEndHTML:########\r\nStartFragment:########\r\nEndFragment:########\r\n";
        string pre = "<html><body><!--StartFragment-->";
        string post = "<!--EndFragment--></body></html>";
        string full = header + pre + html + post;

        int startHTML = header.Length;
        int endHTML = full.Length;
        int startFragment = header.Length + pre.Length;
        int endFragment = full.Length - post.Length;

        string fmt(int n) => n.ToString("D8");
        full = full.Replace("StartHTML:########", $"StartHTML:{fmt(startHTML)}")
                   .Replace("EndHTML:########", $"EndHTML:{fmt(endHTML)}")
                   .Replace("StartFragment:########", $"StartFragment:{fmt(startFragment)}")
                   .Replace("EndFragment:########", $"EndFragment:{fmt(endFragment)}");
        return full;
    }

    // ---------------- IMAGE ----------------
    static int GetImage(string[] args)
    {
        if (!Clipboard.ContainsImage())
        {
            // If image not present, output empty and succeed (exit 0) for easier scripting
            return 0;
        }

        using var img = Clipboard.GetImage();
        string format = (GetOpt(args, "--format", "png") ?? "png").ToLowerInvariant();

        if (format != "png" && format != "bmp")
            return Fail("get-image: --format must be png or bmp");

        using var ms = new MemoryStream();
        if (format == "png") img.Save(ms, ImageFormat.Png);
        else img.Save(ms, ImageFormat.Bmp);

        var outPath = GetOpt(args, "--out");
        if (!string.IsNullOrEmpty(outPath))
        {
            File.WriteAllBytes(outPath, ms.ToArray());
        }
        else
        {
            // Base64 to STDOUT (no BOM, no newline)
            Console.OpenStandardOutput().Write(ms.ToArray(), 0, (int)ms.Length);
            // If you prefer base64 text: Console.Write(Convert.ToBase64String(ms.ToArray()));
        }
        return 0;
    }

    // static int SetImage(string[] args)
    // {
    //     string? path = GetOpt(args, "--from-file");
    //     if (string.IsNullOrEmpty(path))
    //         return Fail("set-image requires --from-file <imagePath>");

    //     using var img = Image.FromFile(path);
    //     Clipboard.SetImage(img);
    //     return 0;
    // }

    static int SetImage(string[] args)
    {
        try
        {
            string? path = GetOpt(args, "--from-file");
            bool fromStdin = HasFlag(args, "--from-stdin");

            if (fromStdin)
            {
                using var ms = new MemoryStream();
                // read raw PNG/BMP/JPEG bytes from STDIN
                Console.OpenStandardInput().CopyTo(ms);
                ms.Position = 0;
                using var img = Image.FromStream(ms, useEmbeddedColorManagement: true, validateImageData: true);
                Clipboard.SetImage(img);
                return 0;
            }

            if (string.IsNullOrEmpty(path))
                return Fail("set-image requires --from-file <imagePath> or --from-stdin");

            if (!File.Exists(path))
                return Fail($"Image file not found: {path}");

            using var imgFromFile = Image.FromFile(path);
            Clipboard.SetImage(imgFromFile);
            return 0;
        }
        catch (Exception ex)
        {
            return Fail("set-image error: " + ex.Message);
        }
    }

    // ---------------- FILES ----------------
    static int GetFiles(string[] args)
    {
        if (!Clipboard.ContainsFileDropList())
            return 0;

        var files = Clipboard.GetFileDropList().Cast<string>().ToArray();
        if (HasFlag(args, "--json"))
        {
            Console.Write(JsonSerializer.Serialize(files));
        }
        else
        {
            // newline separated
            foreach (var f in files) Console.WriteLine(f);
        }
        return 0;
    }

    static int SetFiles(string[] args)
    {
        string? pathsJoined = GetOpt(args, "--paths");
        string? listFile = GetOpt(args, "--from-filelist");

        string[]? paths = null;

        if (!string.IsNullOrEmpty(pathsJoined))
            paths = pathsJoined.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        else if (!string.IsNullOrEmpty(listFile))
            paths = File.ReadAllLines(listFile).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

        if (paths == null || paths.Length == 0)
            return Fail("set-files requires --paths \"p1|p2|...\" or --from-filelist <path>");

        var col = new System.Collections.Specialized.StringCollection();
        foreach (var p in paths) col.Add(p);
        Clipboard.SetFileDropList(col);
        return 0;
    }
}
