using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     The stream overloads read into a rented buffer that starts at 80 KB when the length is
///     unknown and doubles as needed: an input past that size still arrives whole, and one past
///     <see cref="ImagingOptions.MaxInputBytes" /> is refused, whether the stream can seek or not.
/// </summary>
public sealed class TheStreamInputIsBoundedTests
{
    private const int Trailing = 200 * 1024;

    /// <summary>A PNG followed by 200 KB the decoder ignores, so the read must grow its buffer.</summary>
    private static readonly byte[] PaddedPng = [.. TestHelper.CreateTestPng(64, 48), .. new byte[Trailing]];

    /// <summary>A non-seekable wrapper, so the length is unknown up front.</summary>
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [NativeRequiredFact]
    public void Load_NonSeekableInputLargerThanTheFirstBuffer_LoadsTheImage()
    {
        using var pipeline = ImagePipeline.Load(new NonSeekableStream(PaddedPng));

        pipeline.Width.Should().Be(64);
        pipeline.Height.Should().Be(48);
    }

    [NativeRequiredFact]
    public async Task LoadAsync_NonSeekableInputLargerThanTheFirstBuffer_LoadsTheImage()
    {
        using var pipeline = await ImagePipeline.LoadAsync(new NonSeekableStream(PaddedPng));

        pipeline.Width.Should().Be(64);
    }

    [NativeRequiredFact]
    public void Load_NonSeekableInputOverTheLimit_IsRefused()
    {
        var options = new ImagingOptions { MaxInputBytes = 100 * 1024 };

        var act = () => ImagePipeline.Load(new NonSeekableStream(PaddedPng), options);

        act.Should().Throw<ImagingException>().Which.Reason.Should().Be(ImagingError.InputTooLarge);
    }

    [NativeRequiredFact]
    public async Task LoadAsync_SeekableInputOverTheLimit_IsRefusedBeforeReading()
    {
        var options = new ImagingOptions { MaxInputBytes = 100 * 1024 };
        using var input = new MemoryStream(PaddedPng);

        var act = () => ImagePipeline.LoadAsync(input, options: options);

        (await act.Should().ThrowAsync<ImagingException>()).Which.Reason.Should().Be(ImagingError.InputTooLarge);
        input.Position.Should().Be(0);
    }

    [NativeRequiredFact]
    public void Load_InputExactlyAtTheLimit_Loads()
    {
        var options = new ImagingOptions { MaxInputBytes = PaddedPng.Length };

        using var pipeline = ImagePipeline.Load(new NonSeekableStream(PaddedPng), options);

        pipeline.Width.Should().Be(64);
    }

    [NativeRequiredFact]
    public void InfoFromStream_NonSeekableWithALargeLimit_ReadsPastTheFirstBufferAndKeepsTheHeader()
    {
        var info = ImageInfo.FromStream(new NonSeekableStream(PaddedPng), maxBytes: 1024 * 1024);

        info.Width.Should().Be(64);
        info.Height.Should().Be(48);
    }
}
