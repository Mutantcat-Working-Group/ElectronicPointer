using System.Text.Json;
using System.Text.Json.Serialization;
using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.Ink;

namespace Mutantcat.ElectronicPointer.Core.Serialization;

/// <summary>
/// Reads and writes the board document as JSON. The format is intentionally explicit
/// rather than reflective so that older files keep loading on every platform and a
/// format bump is a deliberate change.
/// </summary>
public static class BoardDocumentSerializer
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string Serialize(BoardDocument document, string applicationVersion, string applicationId)
    {
        var dto = new DocumentDto
        {
            FormatVersion = FormatVersion,
            ApplicationId = applicationId,
            ApplicationVersion = applicationVersion,
            ActiveIndex = document.ActiveIndex,
        };

        foreach (var page in document.Pages)
            dto.Pages.Add(ToDto(page));

        return JsonSerializer.Serialize(dto, WriteOptions);
    }

    /// <summary>Deserializes a document, returning null when the payload is unusable.</summary>
    public static BoardDocument? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        DocumentDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DocumentDto>(json, ReadOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (dto?.Pages is null || dto.Pages.Count == 0)
            return null;

        if (dto.FormatVersion > FormatVersion)
            return null;

        var document = new BoardDocument();
        foreach (var pageDto in dto.Pages)
        {
            var page = ToModel(pageDto);
            document.Pages.Add(page);
            page.Owner = document;
        }

        document.ActiveIndex = dto.ActiveIndex;
        document.Touch();
        return document;
    }

    private static PageDto ToDto(BoardPage page)
    {
        var dto = new PageDto
        {
            Name = page.Name,
            BackgroundColor = page.BackgroundColor,
        };

        foreach (var stroke in page.Strokes)
            dto.Strokes.Add(ToDto(stroke));

        return dto;
    }

    private static StrokeDto ToDto(Stroke stroke)
    {
        var dto = new StrokeDto
        {
            Id = stroke.Id,
            Kind = stroke.Style.Kind.ToString(),
            Color = stroke.Style.Color,
            Size = stroke.Style.Size,
            Thinning = stroke.Style.Thinning,
            SimulatePressure = stroke.Style.SimulatePressure,
            Smoothing = stroke.Style.Smoothing,
            Streamline = stroke.Style.Streamline,
            TaperEnds = stroke.Style.TaperEnds,
            Points = new double[stroke.Samples.Count][],
        };

        for (var i = 0; i < stroke.Samples.Count; i++)
        {
            var sample = stroke.Samples[i];
            dto.Points[i] = new[] { sample.Point.X, sample.Point.Y, sample.Pressure };
        }

        return dto;
    }

    private static BoardPage ToModel(PageDto dto)
    {
        var page = new BoardPage { Name = dto.Name ?? "页面" };
        page.BackgroundColor = dto.BackgroundColor;

        if (dto.Strokes is null)
            return page;

        foreach (var strokeDto in dto.Strokes)
        {
            if (ToModel(strokeDto) is { } stroke)
            {
                page.Strokes.Add(stroke);
            }
        }

        page.Touch();
        return page;
    }

    private static Stroke? ToModel(StrokeDto dto)
    {
        if (dto.Points is null || dto.Points.Length == 0)
            return null;

        var kind = Enum.TryParse<StrokeKind>(dto.Kind, ignoreCase: true, out var parsed) ? parsed : StrokeKind.Pen;
        var style = new StrokeStyle(
            kind,
            dto.Color,
            dto.Size <= 0 ? StrokeStyle.DefaultPen().Size : dto.Size,
            double.IsNaN(dto.Thinning) || dto.Thinning < 0 ? 0.5 : dto.Thinning,
            dto.SimulatePressure,
            dto.Smoothing is > 0 and <= 1 ? dto.Smoothing : 0.5,
            dto.Streamline is > 0 and <= 1 ? dto.Streamline : 0.5,
            dto.TaperEnds);

        var stroke = new Stroke(style) { Id = dto.Id ?? Guid.NewGuid() };

        foreach (var point in dto.Points)
        {
            if (point is null || point.Length < 2)
                continue;

            stroke.Append(new Geometry.Vec2(point[0], point[1]), point.Length > 2 ? point[2] : null);
        }

        stroke.Complete();
        return stroke;
    }

    private sealed class DocumentDto
    {
        public int FormatVersion { get; set; }

        public string? ApplicationId { get; set; }

        public string? ApplicationVersion { get; set; }

        public int ActiveIndex { get; set; }

        public List<PageDto> Pages { get; set; } = new();
    }

    private sealed class PageDto
    {
        public string? Name { get; set; }

        public uint BackgroundColor { get; set; }

        public List<StrokeDto> Strokes { get; set; } = new();
    }

    private sealed class StrokeDto
    {
        public Guid? Id { get; set; }

        public string? Kind { get; set; }

        public uint Color { get; set; }

        public double Size { get; set; }

        public double Thinning { get; set; }

        public bool SimulatePressure { get; set; } = true;

        public double Smoothing { get; set; } = 0.5;

        public double Streamline { get; set; } = 0.5;

        public bool TaperEnds { get; set; }

        public double[][]? Points { get; set; }
    }
}
