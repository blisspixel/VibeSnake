using System.Collections.Generic;
using Godot;
using VibeSnake.Rules;

namespace VibeSnake.Game;

/// <summary>
/// Two-frame personal-best signature. Frame 0 is the approved logo pose.
/// Frame 1 is that same crop with the sunglasses one art cell lower.
/// The motion lasts under a second and never repeats. Reduced motion and
/// flash-free play stay on the lowered frame. The caller draws it; this
/// type does not wait, and it does not take input.
/// </summary>
internal static class ShadesTip
{
    public const double MotionSeconds = 0.45;
    public const int OriginalFrame = 0;
    public const int LoweredFrame = 1;
    public const int RunEndScale = 2;
    public const int ScoresScale = 1;

    public static bool Shows(bool isNewPersonalBest) => isNewPersonalBest;

    public static int FrameIndex(double elapsedSeconds, bool reducedMotion, bool flashFree)
    {
        if (reducedMotion || flashFree)
        {
            return LoweredFrame;
        }

        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }

        return elapsedSeconds < MotionSeconds ? OriginalFrame : LoweredFrame;
    }

    public static bool ShowsLoweredFrame(
        double elapsedSeconds,
        bool reducedMotion,
        bool flashFree,
        bool alreadyLowered)
    {
        if (alreadyLowered || reducedMotion || flashFree)
        {
            return true;
        }

        return FrameIndex(elapsedSeconds, reducedMotion: false, flashFree: false) == LoweredFrame;
    }
}

internal readonly record struct ShadesTipPixel(byte R, byte G, byte B, byte A)
{
    public bool IsShade => A > 0 && R < 55 && G < 70 && B < 90;

    public int PackedRgb => (R << 16) | (G << 8) | B;
}

/// <summary>
/// Cell crop of the approved 1024 logo. The lattice, crop, and shade rows
/// are measured from that mark: each art cell is 16 source pixels, and the
/// sunglasses are the solid dark bar on grid rows 9 and 10. The logo file
/// itself is not rewritten.
/// </summary>
internal static class ShadesTipFrames
{
    public const int SourceSize = 1024;
    public const int Cell = 16;
    public const int GridSize = SourceSize / Cell;
    public const int CropLeft = 4;
    public const int CropTop = 5;
    public const int CropRight = 59;
    public const int CropBottom = 38;
    public const int ShadeRow = 9;
    public const int ShadeRowCount = 2;
    public const int LoweredBy = 1;
    public const int HeadLeft = 20;
    public const int HeadTop = 5;
    public const int HeadRight = 58;
    public const int HeadBottom = 22;
    public const int MinimumShadeColumns = 24;

    public static ShadesTipAtlas Build(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width != SourceSize || height != SourceSize)
        {
            throw new ArgumentException(
                $"The personal-best signature requires the {SourceSize} logo.",
                nameof(width));
        }

        var expected = checked(width * height * 4);
        if (rgba.Length != expected)
        {
            throw new ArgumentException(
                $"Logo pixels were {rgba.Length} bytes, expected {expected}.",
                nameof(rgba));
        }

        var grid = new ShadesTipPixel[GridSize * GridSize];
        var palette = new HashSet<int>();
        for (var gy = 0; gy < GridSize; gy++)
        {
            for (var gx = 0; gx < GridSize; gx++)
            {
                var pixel = MedianCell(rgba, width, gx, gy);
                grid[(gy * GridSize) + gx] = pixel;
                palette.Add(pixel.PackedRgb);
            }
        }

        var shadeColumns = new List<int>();
        for (var x = 0; x < GridSize; x++)
        {
            if (grid[(ShadeRow * GridSize) + x].IsShade
                && grid[((ShadeRow + 1) * GridSize) + x].IsShade)
            {
                shadeColumns.Add(x);
            }
        }

        if (shadeColumns.Count < MinimumShadeColumns)
        {
            throw new InvalidOperationException(
                "The approved logo no longer has the sunglasses bar used by the signature.");
        }

        var loweredGrid = (ShadesTipPixel[])grid.Clone();
        for (var row = ShadeRow + ShadeRowCount - 1; row >= ShadeRow; row--)
        {
            foreach (var x in shadeColumns)
            {
                loweredGrid[((row + LoweredBy) * GridSize) + x] = grid[(row * GridSize) + x];
                loweredGrid[(row * GridSize) + x] = grid[((row - LoweredBy) * GridSize) + x];
            }
        }

        var cropWidth = CropRight - CropLeft;
        var cropHeight = CropBottom - CropTop;
        return new ShadesTipAtlas(
            cropWidth,
            cropHeight,
            Crop(grid, cropWidth, cropHeight),
            Crop(loweredGrid, cropWidth, cropHeight),
            HeadLeft - CropLeft,
            HeadTop - CropTop,
            HeadRight - HeadLeft,
            HeadBottom - HeadTop,
            palette);
    }

    public static Image TextureImage(ShadesTipPixel[] pixels, int width, int height)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[(y * width) + x];
                image.SetPixel(
                    x,
                    y,
                    new Color(pixel.R / 255f, pixel.G / 255f, pixel.B / 255f, pixel.A / 255f));
            }
        }

        return image;
    }

    private static ShadesTipPixel[] Crop(ShadesTipPixel[] grid, int width, int height)
    {
        var pixels = new ShadesTipPixel[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = grid[((y + CropTop) * GridSize) + x + CropLeft];
                pixels[(y * width) + x] = IsField(source)
                    ? new ShadesTipPixel(0, 0, 0, 0)
                    : source;
            }
        }

        return pixels;
    }

    private static ShadesTipPixel MedianCell(ReadOnlySpan<byte> rgba, int width, int cellX, int cellY)
    {
        var samples = new[]
        {
            Sample(rgba, width, (cellX * Cell) + 6, (cellY * Cell) + 6),
            Sample(rgba, width, (cellX * Cell) + 10, (cellY * Cell) + 6),
            Sample(rgba, width, (cellX * Cell) + 6, (cellY * Cell) + 10),
            Sample(rgba, width, (cellX * Cell) + 10, (cellY * Cell) + 10),
        };

        Array.Sort(samples, ComparePixels);
        return samples[2];
    }

    private static ShadesTipPixel Sample(ReadOnlySpan<byte> rgba, int width, int x, int y)
    {
        var start = ((y * width) + x) * 4;
        return new ShadesTipPixel(rgba[start], rgba[start + 1], rgba[start + 2], rgba[start + 3]);
    }

    private static int ComparePixels(ShadesTipPixel left, ShadesTipPixel right)
    {
        var order = left.R.CompareTo(right.R);
        if (order != 0)
        {
            return order;
        }

        order = left.G.CompareTo(right.G);
        if (order != 0)
        {
            return order;
        }

        order = left.B.CompareTo(right.B);
        return order != 0 ? order : left.A.CompareTo(right.A);
    }

    private static bool IsField(ShadesTipPixel pixel) =>
        pixel.R > 140
        && pixel.G > 90
        && pixel.B < 130
        && pixel.G > pixel.B + 25
        && pixel.R + 25 > pixel.G;
}

internal sealed class ShadesTipAtlas
{
    public ShadesTipAtlas(
        int width,
        int height,
        ShadesTipPixel[] original,
        ShadesTipPixel[] lowered,
        int headX,
        int headY,
        int headWidth,
        int headHeight,
        HashSet<int> palette)
    {
        Width = width;
        Height = height;
        Original = original;
        Lowered = lowered;
        HeadX = headX;
        HeadY = headY;
        HeadWidth = headWidth;
        HeadHeight = headHeight;
        Palette = palette;
    }

    public int Width { get; }

    public int Height { get; }

    public ShadesTipPixel[] Original { get; }

    public ShadesTipPixel[] Lowered { get; }

    public int HeadX { get; }

    public int HeadY { get; }

    public int HeadWidth { get; }

    public int HeadHeight { get; }

    public HashSet<int> Palette { get; }

    public Rect2 HeadSource => new(HeadX, HeadY, HeadWidth, HeadHeight);
}

/// <summary>
/// Shared placement for the run-end card and the Local Scores still.
/// Sprite sizes stay an integer multiple of the art cell. Text scale
/// moves only the words.
/// </summary>
internal static class ShadesTipLayout
{
    public const float LabelGap = 10f;
    public const int LabelBaseFontSize = 15;
    public const float RunEndSpriteLeft = 964f;
    public const float RunEndSpriteTop = 114f;
    public const float OutcomeLeft = 238f;
    public const float OutcomeBaseline = 172f;
    public const int OutcomeBaseFontSize = 30;
    public const float CauseBaseline = 214f;
    public const int CauseBaseFontSize = 18;
    public const float RecoveryBaseline = 270f;
    public const int RecoveryBaseFontSize = 12;
    public const float ScoresIdentityLeft = 46f;
    public const float ScoresBestPreferredLeft = 900f;
    public const float ScoresBaseline = 214f;
    public const float ScoresCounterBaseline = 184f;
    public const float ScoresRight = 1264f;
    public const float ScoresGap = 8f;
    public static readonly Rect2 RunEndOverlay = new(190f, 112f, 900f, 542f);

    public static Rect2 RunEndSprite(int cellWidth, int cellHeight) =>
        new(
            RunEndSpriteLeft,
            RunEndSpriteTop,
            cellWidth * ShadesTip.RunEndScale,
            cellHeight * ShadesTip.RunEndScale);

    public static Vector2 RunEndLabel(Font font, int fontSize, string text, Rect2 sprite)
    {
        ArgumentNullException.ThrowIfNull(font);
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1f, fontSize).X;
        return new Vector2(sprite.Position.X - LabelGap - width, sprite.Position.Y + 40f);
    }

    public static bool TryPlaceScoresMarker(
        Font font,
        string identity,
        int identityFontSize,
        string bestText,
        int bestFontSize,
        int counterFontSize,
        int headWidth,
        int headHeight,
        out Rect2 sprite,
        out Vector2 textPosition)
    {
        ArgumentNullException.ThrowIfNull(font);
        var spriteWidth = headWidth * ShadesTip.ScoresScale;
        var spriteHeight = headHeight * ShadesTip.ScoresScale;
        var spriteTop = ScoresBaseline - spriteHeight + 2f;
        var counterBottom = ScoresCounterBaseline + font.GetDescent(counterFontSize);
        var textWidth = font.GetStringSize(bestText, HorizontalAlignment.Left, -1f, bestFontSize).X;
        var textLeft = ScoresBestPreferredLeft;
        var spriteLeft = textLeft + textWidth + ScoresGap;
        if (spriteLeft + spriteWidth > ScoresRight)
        {
            spriteLeft = ScoresRight - spriteWidth;
            textLeft = spriteLeft - ScoresGap - textWidth;
        }

        var identityRight = ScoresIdentityLeft + font.GetStringSize(
            identity,
            HorizontalAlignment.Left,
            -1f,
            identityFontSize).X;
        var fits = spriteTop >= counterBottom + 2f
            && textLeft >= identityRight + ScoresGap
            && spriteLeft + spriteWidth <= ScoresRight + 0.01f
            && textLeft >= ScoresIdentityLeft;
        if (!fits)
        {
            sprite = default;
            textPosition = new Vector2(ScoresBestPreferredLeft, ScoresBaseline);
            return false;
        }

        sprite = new Rect2(spriteLeft, spriteTop, spriteWidth, spriteHeight);
        textPosition = new Vector2(textLeft, ScoresBaseline);
        return true;
    }
}

internal static class ShadesTipQualification
{
    public const string CopyId = "run-end.new-best";

    public static void Verify(Font font, ShadesTipAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(atlas);
        VerifyClock();
        VerifyFrames(atlas);
        VerifyRunEndLayout(font, atlas);
        VerifyScoresLayout(font, atlas);
        var proof = System.Environment.GetEnvironmentVariable("VIBESNAKE_SHADES_PROOF");
        if (!string.IsNullOrWhiteSpace(proof))
        {
            WriteProof(proof, atlas);
        }
    }

    private static void VerifyClock()
    {
        if (ShadesTip.MotionSeconds <= 0 || ShadesTip.MotionSeconds >= 1.0)
        {
            throw new InvalidOperationException("The shades tip must finish in under a second.");
        }

        if (ShadesTip.RunEndScale < 1 || ShadesTip.ScoresScale < 1)
        {
            throw new InvalidOperationException("The shades tip scale must be a positive integer.");
        }

        if (ShadesTip.Shows(isNewPersonalBest: false) || !ShadesTip.Shows(isNewPersonalBest: true))
        {
            throw new InvalidOperationException("The shades tip is only for a new personal best.");
        }

        if (ShadesTip.FrameIndex(0, reducedMotion: false, flashFree: false) != ShadesTip.OriginalFrame
            || ShadesTip.FrameIndex(
                ShadesTip.MotionSeconds - 0.001,
                reducedMotion: false,
                flashFree: false) != ShadesTip.OriginalFrame
            || ShadesTip.FrameIndex(
                ShadesTip.MotionSeconds,
                reducedMotion: false,
                flashFree: false) != ShadesTip.LoweredFrame
            || ShadesTip.FrameIndex(2, reducedMotion: false, flashFree: false) != ShadesTip.LoweredFrame)
        {
            throw new InvalidOperationException("The shades tip does not play once and then hold.");
        }

        if (ShadesTip.FrameIndex(0, reducedMotion: true, flashFree: false) != ShadesTip.LoweredFrame
            || ShadesTip.FrameIndex(0, reducedMotion: false, flashFree: true) != ShadesTip.LoweredFrame
            || ShadesTip.FrameIndex(double.NaN, reducedMotion: true, flashFree: true) != ShadesTip.LoweredFrame)
        {
            throw new InvalidOperationException(
                "Reduced motion and flash-free play must show the lowered shades immediately.");
        }

        if (ShadesTip.ShowsLoweredFrame(
                0,
                reducedMotion: false,
                flashFree: false,
                alreadyLowered: false)
            || !ShadesTip.ShowsLoweredFrame(
                ShadesTip.MotionSeconds,
                reducedMotion: false,
                flashFree: false,
                alreadyLowered: false)
            || !ShadesTip.ShowsLoweredFrame(
                0,
                reducedMotion: false,
                flashFree: false,
                alreadyLowered: true)
            || !ShadesTip.ShowsLoweredFrame(
                double.NaN,
                reducedMotion: false,
                flashFree: false,
                alreadyLowered: true))
        {
            throw new InvalidOperationException(
                "The shades tip reversed or started on the lowered frame while motion is allowed.");
        }

        foreach (var illegal in new[] { double.NaN, double.PositiveInfinity, -0.01 })
        {
            if (!Throws<ArgumentOutOfRangeException>(() =>
                    ShadesTip.FrameIndex(illegal, reducedMotion: false, flashFree: false)))
            {
                throw new InvalidOperationException(
                    "An unreadable shades-tip clock must fail closed while motion is allowed.");
            }
        }

        var gate = new RestartIntentGate();
        gate.NoteTerminal(4);
        _ = ShadesTip.FrameIndex(0, reducedMotion: false, flashFree: false);
        if (!gate.CanRestart(5) || gate.CanRestart(4))
        {
            throw new InvalidOperationException("The shades tip changed the run-end restart gate.");
        }

        if (ShellLocalization.Format(CopyId, ShellLocale.English) != "NEW BEST")
        {
            throw new InvalidOperationException("The personal-best signature words changed.");
        }
    }

    private static void VerifyFrames(ShadesTipAtlas atlas)
    {
        if (atlas.Width != ShadesTipFrames.CropRight - ShadesTipFrames.CropLeft
            || atlas.Height != ShadesTipFrames.CropBottom - ShadesTipFrames.CropTop
            || atlas.Original.Length != atlas.Width * atlas.Height
            || atlas.Lowered.Length != atlas.Original.Length)
        {
            throw new InvalidOperationException("The shades-tip crop does not match the logo lattice.");
        }

        var differs = false;
        var opaque = 0;
        for (var index = 0; index < atlas.Original.Length; index++)
        {
            var original = atlas.Original[index];
            var lowered = atlas.Lowered[index];
            differs |= original.R != lowered.R
                || original.G != lowered.G
                || original.B != lowered.B
                || original.A != lowered.A;
            foreach (var pixel in new[] { original, lowered })
            {
                if (pixel.A == 0)
                {
                    continue;
                }

                opaque++;
                if (!atlas.Palette.Contains(pixel.PackedRgb))
                {
                    throw new InvalidOperationException(
                        "The shades tip introduced a colour that is not in the logo.");
                }
            }
        }

        if (!differs || opaque == 0)
        {
            throw new InvalidOperationException("The lowered shades frame is not a distinct logo crop.");
        }

        var shadeTop = ShadesTipFrames.ShadeRow - ShadesTipFrames.CropTop;
        var arrived = shadeTop + ShadesTipFrames.ShadeRowCount;
        var originalBar = CountShade(atlas.Original, atlas, shadeTop);
        var loweredForehead = CountShade(atlas.Lowered, atlas, shadeTop);
        var originalBelow = CountShade(atlas.Original, atlas, arrived);
        var loweredBar = CountShade(atlas.Lowered, atlas, arrived);
        if (originalBar < ShadesTipFrames.MinimumShadeColumns
            || loweredForehead > originalBar / 3
            || loweredBar < originalBar)
        {
            throw new InvalidOperationException(
                "The sunglasses did not move down by one art cell. "
                + $"bar={originalBar} forehead={loweredForehead} "
                + $"below={originalBelow} arrived={loweredBar}.");
        }

        VerifyHeadCrop(atlas);
    }

    private static void VerifyHeadCrop(ShadesTipAtlas atlas)
    {
        var neck = atlas.HeadY + atlas.HeadHeight;
        if (atlas.HeadX < 0
            || atlas.HeadY < 0
            || atlas.HeadWidth < ShadesTipFrames.MinimumShadeColumns
            || neck >= atlas.Height
            || atlas.HeadX + atlas.HeadWidth > atlas.Width)
        {
            throw new InvalidOperationException("The Local Scores head is outside the logo crop.");
        }

        var jaw = neck - 1;
        var jawShade = CountShade(atlas.Original, atlas, jaw);
        var jawSkin = CountSkin(atlas.Original, atlas, jaw);
        var neckShade = CountShade(atlas.Original, atlas, neck);
        if (jawSkin <= jawShade || neckShade <= jawShade)
        {
            throw new InvalidOperationException(
                "The Local Scores head clips the jaw or includes the neck. "
                + $"jawSkin={jawSkin} jawShade={jawShade} neckShade={neckShade}.");
        }
    }

    private static void VerifyRunEndLayout(Font font, ShadesTipAtlas atlas)
    {
        var sprite = ShadesTipLayout.RunEndSprite(atlas.Width, atlas.Height);
        if (sprite.Size.X != atlas.Width * ShadesTip.RunEndScale
            || sprite.Size.Y != atlas.Height * ShadesTip.RunEndScale
            || sprite.Size.X != MathF.Round(sprite.Size.X)
            || sprite.Size.Y != MathF.Round(sprite.Size.Y))
        {
            throw new InvalidOperationException("The run-end snake is not drawn at an integer scale.");
        }

        var overlay = ShadesTipLayout.RunEndOverlay;
        if (sprite.Position.X < overlay.Position.X + 8f
            || sprite.Position.Y < overlay.Position.Y
            || sprite.End.X > overlay.End.X - 8f
            || sprite.End.Y > overlay.End.Y)
        {
            throw new InvalidOperationException("The shades tip leaves the run-end card.");
        }

        var fontSize = DrawnFont(ShadesTipLayout.LabelBaseFontSize);
        foreach (var locale in new[] { ShellLocale.English, ShellLocale.Pseudo })
        {
            var text = ShellLocalization.Format(ShadesTipQualification.CopyId, locale);
            var labelAt = ShadesTipLayout.RunEndLabel(font, fontSize, text, sprite);
            var label = TextBox(font, text, labelAt, fontSize);
            if (Overlaps(label, sprite) || label.Position.X < overlay.Position.X + 8f)
            {
                throw new InvalidOperationException(
                    $"NEW BEST overlaps the snake or the card edge in {locale} "
                    + $"at x={label.Position.X:0.0}.");
            }

            foreach (var outcome in new[] { "RUN ENDED", "GRID COMPLETE" })
            {
                var outcomeBox = TextBox(
                    font,
                    outcome,
                    new Vector2(ShadesTipLayout.OutcomeLeft, ShadesTipLayout.OutcomeBaseline),
                    DrawnFont(ShadesTipLayout.OutcomeBaseFontSize));
                if (Overlaps(outcomeBox, label) || Overlaps(outcomeBox, sprite))
                {
                    throw new InvalidOperationException(
                        $"The shades tip overlaps the run-end outcome '{outcome}' in {locale}.");
                }
            }
        }

        var cause = TextBox(
            font,
            "CAUSE: SELF COLLISION",
            new Vector2(ShadesTipLayout.OutcomeLeft, ShadesTipLayout.CauseBaseline),
            DrawnFont(ShadesTipLayout.CauseBaseFontSize));
        var recovery = TextBox(
            font,
            "RECOVERY: Shield, Phase Shift, or Last Stand can prevent a body collision.",
            new Vector2(ShadesTipLayout.OutcomeLeft, ShadesTipLayout.RecoveryBaseline),
            DrawnFont(ShadesTipLayout.RecoveryBaseFontSize));
        if (Overlaps(cause, sprite) || Overlaps(recovery, sprite))
        {
            throw new InvalidOperationException(
                "The shades tip overlaps the run-end cause or recovery line. "
                + $"spriteBottom={sprite.End.Y:0.0} causeTop={cause.Position.Y:0.0} "
                + $"recoveryRight={recovery.End.X:0.0}.");
        }
    }

    private static void VerifyScoresLayout(Font font, ShadesTipAtlas atlas)
    {
        if (atlas.HeadWidth * ShadesTip.ScoresScale != atlas.HeadWidth
            || atlas.HeadHeight * ShadesTip.ScoresScale != atlas.HeadHeight)
        {
            throw new InvalidOperationException("The Local Scores still must stay at 1:1 art cells.");
        }

        var identityFont = DrawnFont(13);
        var bestFont = DrawnFont(13);
        var counterFont = DrawnFont(14);
        var best = "PERSONAL BEST 2000000000";
        var currentIdentity =
            "vibesnake-core@4  vibe-standard-v1-dda-off  DDA OFF  CFG ffffffff";
        if (!ShadesTipLayout.TryPlaceScoresMarker(
                font,
                currentIdentity,
                identityFont,
                best,
                bestFont,
                counterFont,
                atlas.HeadWidth,
                atlas.HeadHeight,
                out var sprite,
                out var textAt)
            || Overlaps(sprite, TextBox(font, best, textAt, bestFont))
            || Overlaps(
                sprite,
                TextBox(
                    font,
                    currentIdentity,
                    new Vector2(ShadesTipLayout.ScoresIdentityLeft, ShadesTipLayout.ScoresBaseline),
                    identityFont)))
        {
            throw new InvalidOperationException(
                "The Local Scores still does not fit beside the current personal best.");
        }

        var legacy = "UNKNOWN HISTORICAL RULES / NONCOMPETITIVE";
        if (!ShadesTipLayout.TryPlaceScoresMarker(
                font,
                legacy,
                identityFont,
                best,
                bestFont,
                counterFont,
                atlas.HeadWidth,
                atlas.HeadHeight,
                out _,
                out _))
        {
            throw new InvalidOperationException(
                "The Local Scores still does not fit a legacy personal best.");
        }

        var crowded = new string('W', 180);
        var crowdedFits = ShadesTipLayout.TryPlaceScoresMarker(
            font,
            crowded,
            identityFont,
            best,
            bestFont,
            counterFont,
            atlas.HeadWidth,
            atlas.HeadHeight,
            out var crowdedSprite,
            out var crowdedText);
        var preferred = new Vector2(
            ShadesTipLayout.ScoresBestPreferredLeft,
            ShadesTipLayout.ScoresBaseline);
        if (crowdedFits
            && Overlaps(
                crowdedSprite,
                TextBox(font, crowded, new Vector2(46f, 214f), identityFont)))
        {
            throw new InvalidOperationException(
                "A long Local Scores identity was allowed to cover the shades still.");
        }

        if (!crowdedFits
            && (Math.Abs(crowdedText.X - preferred.X) > 0.01f
                || Math.Abs(crowdedText.Y - preferred.Y) > 0.01f))
        {
            throw new InvalidOperationException(
                "A crowded Local Scores line moved the personal best off its anchor.");
        }
    }

    private static int CountShade(ShadesTipPixel[] pixels, ShadesTipAtlas atlas, int row)
    {
        var count = 0;
        for (var x = atlas.HeadX; x < atlas.HeadX + atlas.HeadWidth; x++)
        {
            if (pixels[(row * atlas.Width) + x].IsShade)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountSkin(ShadesTipPixel[] pixels, ShadesTipAtlas atlas, int row)
    {
        var count = 0;
        for (var x = atlas.HeadX; x < atlas.HeadX + atlas.HeadWidth; x++)
        {
            var pixel = pixels[(row * atlas.Width) + x];
            if (pixel.A > 0 && !pixel.IsShade)
            {
                count++;
            }
        }

        return count;
    }

    private static int DrawnFont(int baseSize) =>
        Math.Max(10, (int)Math.Round(baseSize * ShellSettings.MaximumTextScale));

    private static Rect2 TextBox(Font font, string text, Vector2 baseline, int fontSize)
    {
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1f, fontSize).X;
        var ascent = font.GetAscent(fontSize);
        var descent = font.GetDescent(fontSize);
        return new Rect2(baseline.X, baseline.Y - ascent, width, ascent + descent);
    }

    private static bool Overlaps(Rect2 left, Rect2 right) =>
        left.Position.X < right.End.X
        && right.Position.X < left.End.X
        && left.Position.Y < right.End.Y
        && right.Position.Y < left.End.Y;

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static void WriteProof(string path, ShadesTipAtlas atlas)
    {
        const int scale = 6;
        var frameWidth = atlas.Width * scale;
        var frameHeight = atlas.Height * scale;
        var image = Image.CreateEmpty(
            (frameWidth * 2) + 24,
            frameHeight + 16,
            false,
            Image.Format.Rgba8);
        image.Fill(new Color(0.02f, 0.035f, 0.03f));
        Blit(image, atlas.Original, atlas.Width, atlas.Height, 8, 8, scale);
        Blit(image, atlas.Lowered, atlas.Width, atlas.Height, frameWidth + 16, 8, scale);
        var error = image.SavePng(path);
        if (error != Error.Ok)
        {
            throw new IOException($"Could not write the shades-tip proof '{path}'.");
        }
    }

    private static void Blit(
        Image image,
        ShadesTipPixel[] pixels,
        int width,
        int height,
        int left,
        int top,
        int scale)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[(y * width) + x];
                if (pixel.A == 0)
                {
                    continue;
                }

                image.FillRect(
                    new Rect2I(left + (x * scale), top + (y * scale), scale, scale),
                    new Color(pixel.R / 255f, pixel.G / 255f, pixel.B / 255f, 1f));
            }
        }
    }
}
