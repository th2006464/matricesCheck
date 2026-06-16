using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MatricesCheck.Models;

namespace MatricesCheck.Pages;

public class CompareModel : PageModel
{
    private readonly IConfiguration _configuration;

    public ValidationResult? ValidationResult { get; set; }
    public List<AnomalyGroup> AnomalyGroups { get; set; } = new();
    public bool HasResult { get; set; }
    public bool ShowColumnConfig { get; set; }
    public string[] Headers { get; set; } = Array.Empty<string>();
    public List<int> AutoDetectedRoleCols { get; set; } = new();
    public string UploadedFileName { get; set; } = "";

    public CompareModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private IActionResult CheckAuth()
    {
        if (!Request.Cookies.ContainsKey("MatricesCheckAuth"))
            return RedirectToPage("/Auth");
        return null!;
    }

    public IActionResult OnGet()
    {
        var auth = CheckAuth();
        if (auth != null!) return auth;

        if (TempData.Peek("RawCsv") != null)
        {
            Headers = TempData.Peek("Headers") as string[] ?? Array.Empty<string>();
            UploadedFileName = TempData.Peek("FileName") as string ?? "";
            AutoDetectedRoleCols = TempData.Peek("AutoRoleCols") as List<int> ?? new();
            ShowColumnConfig = TempData.Peek("ShowConfig") as bool? == true;
        }

        if (TempData.Peek("HasResult") as bool? == true)
        {
            var raw = TempData.Peek("RawCsv") as string;
            var condCols = TempData.Peek("ValidatedConditionCols") as List<int>;
            var roleCols = TempData.Peek("ValidatedRoleCols") as List<int>;
            if (!string.IsNullOrEmpty(raw) && roleCols != null)
            {
                var allRows = RestoreCsv(raw);
                if (allRows.Count >= 2)
                {
                    ValidationResult = CsvValidator.Validate(allRows, condCols ?? new(), roleCols);
                    AnomalyGroups = CsvValidator.BuildAnomalyGroups(ValidationResult);
                    HasResult = true;
                    ShowColumnConfig = false;
                }
            }
        }

        return Page();
    }

    public IActionResult OnPost(IFormFile csvFile)
    {
        var auth = CheckAuth();
        if (auth != null!) return auth;

        if (csvFile == null || csvFile.Length == 0)
        {
            ModelState.AddModelError("csvFile", "请选择一个 CSV 文件上传");
            return Page();
        }

        if (!csvFile.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("csvFile", "仅支持 .csv 格式文件");
            return Page();
        }

        List<string[]> allRows;
        using (var reader = new StreamReader(csvFile.OpenReadStream(), Encoding.UTF8))
        {
            allRows = ParseCsv(reader);
        }

        if (allRows.Count < 2)
        {
            ModelState.AddModelError("csvFile", "CSV 文件至少需要包含表头行和一行数据");
            return Page();
        }

        Headers = allRows[0];
        UploadedFileName = csvFile.FileName;
        var (_, autoRoleCols) = CsvValidator.DetectColumns(Headers);
        AutoDetectedRoleCols = autoRoleCols;
        ShowColumnConfig = true;

        var sb = new StringBuilder();
        foreach (var row in allRows)
        {
            sb.AppendLine(string.Join("\t", row.Select(EscapeForStorage)));
        }
        TempData["RawCsv"] = sb.ToString();
        TempData["FileName"] = csvFile.FileName;
        TempData["Headers"] = Headers;
        TempData["AutoRoleCols"] = AutoDetectedRoleCols;
        TempData["ShowConfig"] = true;

        return RedirectToPage();
    }

    public IActionResult OnPostValidate(int[] conditionCols, int firstRoleCol)
    {
        var auth = CheckAuth();
        if (auth != null!) return auth;

        var raw = TempData["RawCsv"] as string;
        var fileName = TempData["FileName"] as string ?? "result.csv";

        if (string.IsNullOrEmpty(raw))
        {
            ModelState.AddModelError("", "上传数据已过期，请重新上传");
            return Page();
        }

        TempData["RawCsv"] = raw;
        TempData["FileName"] = fileName;

        var allRows = RestoreCsv(raw);
        if (allRows.Count < 2)
            return Page();

        var condColList = conditionCols?.ToList() ?? new List<int>();
        var roleColList = new List<int>();
        if (firstRoleCol >= 0)
        {
            for (int i = firstRoleCol; i < allRows[0].Length; i++)
                roleColList.Add(i);
        }

        ValidationResult = CsvValidator.Validate(allRows, condColList, roleColList);
        AnomalyGroups = CsvValidator.BuildAnomalyGroups(ValidationResult);
        HasResult = true;
        ShowColumnConfig = false;

        var exportCsv = GenerateExportCsv(ValidationResult);
        TempData["ExportCsv"] = exportCsv;
        TempData["ExportFileName"] = Path.GetFileNameWithoutExtension(fileName) + "_checked.csv";
        TempData["HasResult"] = true;
        TempData["ValidatedConditionCols"] = condColList;
        TempData["ValidatedRoleCols"] = roleColList;
        TempData["RawCsv"] = TempData["RawCsv"];
        TempData["FileName"] = TempData["FileName"];

        HasResult = true;
        ShowColumnConfig = false;

        return RedirectToPage();
    }

    public IActionResult OnPostClear()
    {
        TempData.Clear();
        return RedirectToPage("/Compare");
    }

    public IActionResult OnGetDownload()
    {
        var auth = CheckAuth();
        if (auth != null!) return auth;

        var csv = TempData["ExportCsv"] as string;
        var fileName = TempData["ExportFileName"] as string ?? "result.csv";

        if (string.IsNullOrEmpty(csv))
            return RedirectToPage("/Compare");

        TempData["ExportCsv"] = csv;
        TempData["ExportFileName"] = fileName;

        var bytes = Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    // ===== 工具方法（与 Index 相同） =====

    private string EscapeForStorage(string f) =>
        f.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");

    private string UnescapeFromStorage(string f) =>
        f.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");

    private List<string[]> RestoreCsv(string raw)
    {
        var rows = new List<string[]>();
        foreach (var line in raw.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            rows.Add(line.TrimEnd('\r').Split('\t').Select(UnescapeFromStorage).ToArray());
        }
        return rows;
    }

    private List<string[]> ParseCsv(StreamReader reader)
    {
        var rows = new List<string[]>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = new List<string>(); bool inQ = false; var cur = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQ) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else inQ = false; } else cur.Append(c); }
                else { if (c == '"') inQ = true; else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); } else cur.Append(c); }
            }
            fields.Add(cur.ToString()); rows.Add(fields.ToArray());
        }
        return rows;
    }

    private string GenerateExportCsv(ValidationResult result)
    {
        var sb = new StringBuilder();
        sb.Append("存在异常值");
        foreach (var h in result.Headers) { sb.Append(','); sb.Append(EscapeCsvField(h)); }
        sb.AppendLine();
        foreach (var row in result.Rows)
        {
            sb.Append(row.IsAnomaly ? "是" : "否");
            foreach (var cell in row.Cells) { sb.Append(','); sb.Append(EscapeCsvField(cell)); }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string EscapeCsvField(string f)
    {
        if (f.Contains(',') || f.Contains('"') || f.Contains('\n') || f.Contains('\r'))
            return "\"" + f.Replace("\"", "\"\"") + "\"";
        return f;
    }
}
