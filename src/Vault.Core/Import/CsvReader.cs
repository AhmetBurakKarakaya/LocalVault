using System.Text;

namespace Vault.Core.Import;

/// <summary>
/// RFC 4180 CSV okuyucu: tırnaklı alanlar, alan içinde satır sonu ve "" kaçışı.
/// Ayraç (virgül/noktalı virgül/sekme) başlık satırından tahmin edilir; UTF-8 BOM atlanır.
/// </summary>
public static class CsvReader
{
    public static List<string[]> Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿')
            text = text[1..];

        var delimiter = DetectDelimiter(text);
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            if (c == '"' && field.Length == 0 && !fieldStarted)
            {
                inQuotes = true;
                fieldStarted = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                row.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                AddRow(rows, row);
                row = [];
            }
            else
            {
                field.Append(c);
                fieldStarted = true;
            }
        }

        if (inQuotes)
            throw new FormatException("CSV dosyası bozuk: kapanmayan tırnak.");
        if (field.Length > 0 || fieldStarted || row.Count > 0)
        {
            row.Add(field.ToString());
            AddRow(rows, row);
        }
        return rows;
    }

    private static void AddRow(List<string[]> rows, List<string> row)
    {
        // Tamamen boş satırları atla.
        if (row.Any(f => f.Length > 0))
            rows.Add([.. row]);
    }

    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var header = end < 0 ? text : text[..end];
        var inQuotes = false;
        int commas = 0, semicolons = 0, tabs = 0;
        foreach (var c in header)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (inQuotes) continue;
            else if (c == ',') commas++;
            else if (c == ';') semicolons++;
            else if (c == '\t') tabs++;
        }
        return tabs > commas && tabs > semicolons ? '\t' : semicolons > commas ? ';' : ',';
    }
}
