namespace Backend.Api.Services.Common;

using Microsoft.AspNetCore.Http;

public static class FormFileHelper
{
    public static async Task<List<(string FileName, byte[] Content)>> ReadPayloadsAsync(
        IEnumerable<IFormFile>? files,
        CancellationToken ct = default)
    {
        if (files is null)
            return [];

        var payloads = new List<(string FileName, byte[] Content)>();
        foreach (var file in files)
        {
            if (file.Length == 0)
                continue;

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            payloads.Add((file.FileName, ms.ToArray()));
        }

        return payloads;
    }
}
