using System.Diagnostics;

namespace BusinessFlow.Api.Services;

public static class ReportPdfRenderer
{
    public static string Path(IConfiguration configuration) =>
        configuration["Reports:RendererPath"] is { Length: > 0 } value ? value :
        System.IO.Path.Combine(Directory.GetCurrentDirectory(), "ReportRenderer", "ReportRenderer.exe");

    public static async Task<byte[]> RenderAsync(
        string renderer, string report, string rows, CancellationToken cancellationToken)
    {
        var output = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "crm-report-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(renderer)
                {
                    ArgumentList = { report, output },
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            await process.StandardInput.WriteAsync(rows);
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            if (process.ExitCode != 0 || !System.IO.File.Exists(output))
                throw new InvalidOperationException("PDF renderer failed.");
            return await System.IO.File.ReadAllBytesAsync(output, cancellationToken);
        }
        finally
        {
            if (System.IO.File.Exists(output)) System.IO.File.Delete(output);
        }
    }
}
