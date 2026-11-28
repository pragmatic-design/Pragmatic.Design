"""Regenerates the reference artefacts of the visual tests, and their manifest entries, from the tools
recorded for them.

    python -I scripts/visual-references.py photographs   # the photographs Pragmatic.Imaging.Tests encodes
    python -I scripts/visual-references.py metrics       # the image pairs and the values the metrics are held to
    python -I scripts/visual-references.py webp          # WebP files libwebp wrote, for the managed decoder
    python -I scripts/visual-references.py bmp           # the BMP Suite's files, for the managed BMP decoder
    python -I scripts/visual-references.py gif           # GIF files Pillow wrote, for the managed GIF decoder
    python -I scripts/visual-references.py tiff          # libtiff's test images and TIFF files made here, for the managed TIFF decoder
    python -I scripts/visual-references.py ccitt         # fax streams libtiff coded, for the core's CCITT decoder

Needs scikit-image, Pillow and NumPy (pip install scikit-image pillow, in a virtual environment: docs/TESTING.md,
"Images and PDF pages, compared visually"); `metrics` also downloads the CIEDE2000 test data from scikit-image's
repository at the tag of the installed version.

The rule this exists for: a reference is never produced by the code it checks. Every file written here
comes from scikit-image, Pillow or NumPy, never from a Pragmatic assembly, and the manifest beside it
(`references.json`) says which tool, which version and what was done. No test runs this script: a
regenerated reference is a change to commit, and shows up in the diff of the pull request.
"""
import datetime
import json
import shutil
import sys
import urllib.request
from pathlib import Path

import numpy as np
import PIL
import skimage
from PIL import Image, ImageFilter, features
from skimage import color, data, metrics

ROOT = Path(__file__).resolve().parent.parent
PHOTOGRAPHS = ROOT / 'Pragmatic.Imaging' / 'tests' / 'Pragmatic.Imaging.Tests' / 'References' / 'Photographs'
METRICS = ROOT / 'shared' / 'VisualTesting.Tests' / 'References'
WEBP = ROOT / 'Pragmatic.Imaging' / 'tests' / 'Pragmatic.Imaging.Codecs.Tests' / 'References' / 'WebP'
BMP = ROOT / 'Pragmatic.Imaging' / 'tests' / 'Pragmatic.Imaging.Tests' / 'References' / 'Bmp'
GIF = ROOT / 'Pragmatic.Imaging' / 'tests' / 'Pragmatic.Imaging.Tests' / 'References' / 'Gif'
TIFF = ROOT / 'Pragmatic.Imaging' / 'tests' / 'Pragmatic.Imaging.Tests' / 'References' / 'Tiff'
CCITT = ROOT / 'Pragmatic.Imaging' / 'tests' / 'Pragmatic.Imaging.Codecs.Tests' / 'References' / 'Ccitt'
TODAY = datetime.date.today().isoformat()
TOOLS = f'scikit-image {skimage.__version__}, Pillow {PIL.__version__}, NumPy {np.__version__}'
SKIMAGE_DATA = Path(data.__file__).parent

# Each photograph with the licence its own docstring in skimage.data states, quoted.
SOURCES = {
    'astronaut': ('astronaut.png', 'NASA, the astronaut Eileen Collins',
                  'Public domain: "No known copyright restrictions, released into the public domain."'),
    'coffee': ('coffee.png', 'Rachel Michetti, courtesy of Pikolo Espresso Bar',
               'CC0: "No copyright restrictions. CC0 by the photographer (Rachel Michetti)."'),
    'chelsea': ('chelsea.png', 'Stefan van der Walt',
                'CC0: "No copyright restrictions. CC0 by the photographer (Stefan van der Walt)."'),
    'rocket': ('rocket.jpg', 'SpaceX, the launch of DSCOVR on Falcon 9',
               'Public domain: "The image was captured by SpaceX and released in the public domain."'),
}


def write_manifest(directory, entries):
    manifest = {
        'about': 'Every reference in this directory, with what produced it. Regenerate with '
                 'scripts/visual-references.py; a reference is never produced by the code it checks.',
        'references': dict(sorted(entries.items())),
    }
    (directory / 'references.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def photographs():
    PHOTOGRAPHS.mkdir(parents=True, exist_ok=True)
    entries = {}
    for name, (file, author, licence) in SOURCES.items():
        source = SKIMAGE_DATA / file
        target = PHOTOGRAPHS / f'{name}.png'
        if source.suffix == '.png':
            shutil.copyfile(source, target)
            how = f'copied byte for byte from skimage/data/{file}'
        else:
            Image.open(source).convert('RGB').save(target, optimize=True)
            how = f'skimage/data/{file} decoded by Pillow and written as PNG (the source is itself a JPEG)'
        entries[target.name] = {
            'origin': f'scikit-image {skimage.__version__}, skimage.data.{name}: {author}',
            'licence': licence,
            'producedBy': f'scripts/visual-references.py photographs, {TOOLS}: {how}',
            'date': TODAY,
        }
    write_manifest(PHOTOGRAPHS, entries)


def luma(rgb):
    """Rec. 601 luma, the weights JPEG uses, in floating point over 0..255."""
    rgb = rgb.astype(np.float64)
    return 0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]


def ssim(a, b):
    return float(metrics.structural_similarity(a, b, data_range=255, gaussian_weights=True, sigma=1.5,
                                               use_sample_covariance=False))


def measure(reference, distorted):
    """What the C# metrics are held to, each computed by scikit-image or NumPy."""
    difference = np.abs(reference.astype(np.int32) - distorted.astype(np.int32))
    delta_e = color.deltaE_ciede2000(color.rgb2lab(reference), color.rgb2lab(distorted))
    return {
        'maxAbsolute': [int(difference[..., c].max()) for c in range(3)],
        'meanAbsolute': [float(difference[..., c].mean()) for c in range(3)],
        'psnr': float(metrics.peak_signal_noise_ratio(reference, distorted, data_range=255)),
        'ssimLuma': ssim(luma(reference), luma(distorted)),
        'ssimChannels': [ssim(reference[..., c].astype(np.float64), distorted[..., c].astype(np.float64)) for c in range(3)],
        'deltaE2000Mean': float(delta_e.mean()),
        'deltaE2000Max': float(delta_e.max()),
    }


def metric_fixtures():
    METRICS.mkdir(parents=True, exist_ok=True)
    entries = {}
    values = {}
    # Two crops of 128 x 128: skin and hair, then porcelain against wood grain.
    crops = {'astronaut-face': (data.astronaut(), 100, 160), 'coffee-texture': (data.coffee(), 150, 230)}
    rng = np.random.default_rng(1014)
    for name, (image, top, left) in crops.items():
        original = np.ascontiguousarray(image[top:top + 128, left:left + 128, :3])
        source = name.split('-')[0]
        variants = {'original': original}

        pil = Image.fromarray(original)
        jpeg_path = METRICS / f'{name}.jpeg-q25.jpg'
        pil.save(jpeg_path, quality=25)
        variants['jpeg-q25'] = np.asarray(Image.open(jpeg_path).convert('RGB'))
        jpeg_path.unlink()
        variants['blur'] = np.asarray(pil.filter(ImageFilter.GaussianBlur(radius=1.5)))
        noise = rng.integers(-12, 13, size=original.shape)
        variants['noise'] = np.clip(original.astype(np.int32) + noise, 0, 255).astype(np.uint8)

        how = {
            'original': f'a 128 x 128 crop at row {top}, column {left} of skimage.data.{source}',
            'jpeg-q25': 'the original encoded by Pillow as JPEG at quality 25, then decoded by Pillow',
            'blur': 'the original through Pillow ImageFilter.GaussianBlur(radius=1.5)',
            'noise': 'the original plus NumPy default_rng(1014).integers(-12, 13) per sample, clipped to 0..255',
        }
        for variant, pixels in variants.items():
            file = f'{name}.{variant}.png'
            Image.fromarray(pixels).save(METRICS / file)
            entries[file] = {
                'origin': f'scikit-image {skimage.__version__}, skimage.data.{source}',
                'licence': SOURCES[source][2],
                'producedBy': f'scripts/visual-references.py metrics, {TOOLS}: {how[variant]}',
                'date': TODAY,
            }
            if variant != 'original':
                values[f'{name}.original.png|{file}'] = measure(original, pixels)

    (METRICS / 'metrics.json').write_text(json.dumps({
        'about': 'The value of every metric on every pair, computed by the tools named here, which the C# metrics must match.',
        'tools': TOOLS,
        'luma': 'Rec. 601: 0.299 R + 0.587 G + 0.114 B',
        'ssim': 'skimage.metrics.structural_similarity(data_range=255, gaussian_weights=True, sigma=1.5, use_sample_covariance=False)',
        'psnr': 'skimage.metrics.peak_signal_noise_ratio(data_range=255), over the three channels',
        'deltaE2000': 'skimage.color.deltaE_ciede2000 over skimage.color.rgb2lab (D65, 2 degrees)',
        'pairs': values,
    }, indent=2) + '\n', encoding='utf-8')
    entries['metrics.json'] = {
        'origin': 'computed on the image pairs in this directory',
        'licence': 'derived values, no licence of their own',
        'producedBy': f'scripts/visual-references.py metrics, {TOOLS}',
        'date': TODAY,
    }

    tag = f'v{skimage.__version__}'
    url = f'https://raw.githubusercontent.com/scikit-image/scikit-image/{tag}/tests/skimage/color/ciede2000_test_data.txt'
    with urllib.request.urlopen(url) as response:
        (METRICS / 'ciede2000_test_data.txt').write_bytes(response.read())
    entries['ciede2000_test_data.txt'] = {
        'origin': 'G. Sharma, W. Wu, E. N. Dalal, "The CIEDE2000 Color-Difference Formula: Implementation Notes, '
                  'Supplementary Test Data, and Mathematical Observations", Color Research and Application 30(1), 2005; '
                  f'as transcribed by scikit-image at {tag}, tests/skimage/color/ciede2000_test_data.txt',
        'licence': 'published test data; the transcription is part of scikit-image, BSD-3-Clause',
        'producedBy': f'scripts/visual-references.py metrics: downloaded from {url}',
        'date': TODAY,
    }
    write_manifest(METRICS, entries)


def webp_files():
    """WebP files libwebp wrote, through Pillow, for the managed decoder to read: every mode of the format the
    encoder reaches through Pillow's options, on crops of the photographs and on synthetic images."""
    WEBP.mkdir(parents=True, exist_ok=True)
    for old in WEBP.glob('*.webp'):
        old.unlink()
    entries = {}
    libwebp = features.version('webp')
    tools = f'Pillow {PIL.__version__} with libwebp {libwebp}, NumPy {np.__version__}'

    def save(file, image, how, source=None, **options):
        image.save(WEBP / file, 'WEBP', **options)
        settings = ', '.join(f'{key}={value!r}' for key, value in sorted(options.items()) if key not in ('append_images', 'exif'))
        entries[file] = {
            'origin': f'scikit-image {skimage.__version__}, skimage.data.{source}' if source else 'synthetic, made by the script',
            'licence': SOURCES[source][2] if source else 'no licence needed: generated pixels',
            'producedBy': f'scripts/visual-references.py webp, {tools}: {how}, saved with {settings}',
            'date': TODAY,
        }

    # Crops of an odd size, so the last macroblock row and column are partial.
    crops = {
        'astronaut': Image.fromarray(data.astronaut()[60:211, 140:343]),
        'coffee': Image.fromarray(data.coffee()[120:271, 200:403]),
        'chelsea': Image.fromarray(data.chelsea()[40:191, 100:303]),
    }
    for name, crop in crops.items():
        how = f'a 203 x 151 crop of skimage.data.{name}'
        for quality in (20, 75, 95):
            save(f'lossy-{name}-q{quality}.webp', crop, how, name, quality=quality, method=4)
        for method in (0, 6):
            save(f'lossless-{name}-m{method}.webp', crop, how, name, lossless=True, method=method)
    save('lossy-coffee-m0.webp', crops['coffee'], 'a 203 x 151 crop of skimage.data.coffee', 'coffee', quality=60, method=0)
    save('lossy-coffee-m6.webp', crops['coffee'], 'a 203 x 151 crop of skimage.data.coffee', 'coffee', quality=60, method=6)

    # Alpha over a photograph: a radial ramp, and a hard-edged disc that is fully transparent.
    rgb = np.asarray(crops['chelsea'])
    yy, xx = np.mgrid[0:rgb.shape[0], 0:rgb.shape[1]]
    ramp = np.clip(255 - np.hypot(yy - 75, xx - 100) * 2.2, 0, 255).astype(np.uint8)
    ramp[np.hypot(yy - 40, xx - 160) < 18] = 0
    rgba = Image.fromarray(np.dstack([rgb, ramp]))
    how = 'the chelsea crop with a radial alpha ramp and a transparent disc'
    save('lossy-alpha-q75.webp', rgba, how, 'chelsea', quality=75, alpha_quality=100)
    save('lossy-alpha-aq40.webp', rgba, how, 'chelsea', quality=75, alpha_quality=40)
    save('lossless-alpha.webp', rgba, how, 'chelsea', lossless=True)
    save('lossless-alpha-exact.webp', rgba, how + ', the colour under alpha 0 kept', 'chelsea', lossless=True, exact=True)

    # Few colours, so the encoder indexes them: 2, 4 and 16 colours pack 8, 4 and 2 pixels per byte.
    rng = np.random.default_rng(853)
    for colours in (2, 4, 16, 200):
        palette = rng.integers(0, 256, size=(colours, 4), dtype=np.uint8)
        palette[:, 3] = 255 if colours != 16 else palette[:, 3]
        index = (np.add.outer(np.arange(67) // 5, np.arange(45) // 3) + rng.integers(0, 2, size=(67, 45))) % colours
        save(f'lossless-palette-{colours}.webp', Image.fromarray(palette[index]),
             f'a 45 x 67 pattern over {colours} random colours, NumPy default_rng(853)', lossless=True)

    gradient = np.dstack([np.tile(np.arange(256, dtype=np.uint8), (64, 1)),
                          np.tile(np.arange(64, dtype=np.uint8)[:, None] * 4, (1, 256)),
                          np.full((64, 256), 128, np.uint8)])
    save('lossless-gradient.webp', Image.fromarray(gradient), 'a 256 x 64 two-way ramp', lossless=True)
    save('lossy-gradient.webp', Image.fromarray(gradient), 'a 256 x 64 two-way ramp', quality=50)

    # The smallest images, and one row or one column.
    for width, height in ((1, 1), (13, 1), (1, 13), (31, 13)):
        tile = Image.fromarray(rng.integers(0, 256, size=(height, width, 3), dtype=np.uint8))
        how = f'{width} x {height} random pixels, NumPy default_rng(853)'
        save(f'lossy-{width}x{height}.webp', tile, how, quality=80)
        save(f'lossless-{width}x{height}.webp', tile, how, lossless=True)

    # Metadata in the extended container, which the decoder steps over.
    exif = Image.Exif()
    exif[0x0112] = 6
    exif[0x010F] = 'Pragmatic'
    save('lossy-exif.webp', crops['astronaut'], 'the astronaut crop with an EXIF block (orientation 6)', 'astronaut',
         quality=75, exif=exif.tobytes())

    # Animations: the first frame is the image; later frames change a corner.
    base = crops['coffee']
    frames = [base]
    for shift in (1, 2):
        frame = np.asarray(base).copy()
        frame[10:60, 10:90] = np.roll(frame[10:60, 10:90], shift * 17, axis=1)
        frames.append(Image.fromarray(frame))
    how = 'three frames: the coffee crop, then two with a shifted rectangle'
    save('animated-lossy.webp', frames[0], how, 'coffee', save_all=True, append_images=frames[1:], duration=100, loop=0, quality=70)
    save('animated-lossless.webp', frames[0], how, 'coffee', save_all=True, append_images=frames[1:], duration=100, loop=0, lossless=True)
    translucent = [Image.fromarray(np.dstack([np.asarray(f), ramp])) for f in frames]
    save('animated-alpha.webp', translucent[0], how + ', with the chelsea alpha ramp', 'coffee', save_all=True,
         append_images=translucent[1:], duration=80, loop=2, quality=70, background=(255, 0, 0, 255))
    # Every alpha from 0 to 255 over white, in the first frame of an animation: a decoder that blends the
    # first frame onto its transparent canvas, instead of writing it, rounds the colour of the faint ones.
    faint = np.zeros((16, 16, 4), np.uint8)
    faint[..., :3] = 255
    faint[..., 3] = np.arange(256, dtype=np.uint8).reshape(16, 16)
    second = faint.copy()
    second[0:4, 0:4] = (0, 0, 255, 255)
    save('animated-translucent.webp', Image.fromarray(faint), 'two 16 x 16 frames: white at every alpha, then a blue corner',
         save_all=True, append_images=[Image.fromarray(second)], duration=100, lossless=True, exact=True)
    write_manifest(WEBP, entries)


BMP_SUITE = 'https://entropymine.com/jason/bmpsuite/releases/bmpsuite-2.8.zip'
# Their embedded ICC profiles are the one part of the suite its readme leaves out of the public domain.
BMP_WITH_EMBEDDED_PROFILES = {'rgb24prof.bmp', 'rgb24prof2.bmp'}


def bmp_files():
    """Jason Summers' BMP Suite 2.8, the files of its "good" and "questionable" sets, copied byte for byte: the
    decoder is held to what the native library made of each, the ones it refused included."""
    import io
    import zipfile
    BMP.mkdir(parents=True, exist_ok=True)
    for old in BMP.glob('*.bmp'):
        old.unlink()
    with urllib.request.urlopen(BMP_SUITE) as response:
        archive = zipfile.ZipFile(io.BytesIO(response.read()))
    entries = {}
    for member in sorted(archive.namelist()):
        parts = Path(member).parts
        if len(parts) != 3 or parts[1] not in ('g', 'q') or not member.endswith('.bmp'):
            continue
        name = parts[2]
        if name in BMP_WITH_EMBEDDED_PROFILES:
            continue
        (BMP / name).write_bytes(archive.read(member))
        entries[name] = {
            'origin': f'BMP Suite 2.8 by Jason Summers, {parts[1]}/{name}, {BMP_SUITE}',
            'licence': 'Public domain: "Image files generated by this program are not covered by this license, and are '
                       'in the public domain (except for the embedded ICC profiles)." (readme.txt); the files with an '
                       'embedded profile are left out',
            'producedBy': 'scripts/visual-references.py bmp: copied byte for byte from the release archive',
            'date': TODAY,
        }
    write_manifest(BMP, entries)


def gif_files():
    """GIF files Pillow wrote, for the managed decoder to read as the native library read them: palettes of 2 to 256
    colours, interlaced and not, transparency, a local colour table, a comment, the smallest sizes and an animation."""
    GIF.mkdir(parents=True, exist_ok=True)
    for old in GIF.glob('*.gif'):
        old.unlink()
    entries = {}
    tools = f'Pillow {PIL.__version__}, NumPy {np.__version__}'

    def save(file, image, how, source=None, **options):
        image.save(GIF / file, 'GIF', **options)
        settings = ', '.join(f'{key}={value!r}' for key, value in sorted(options.items()) if key != 'append_images')
        entries[file] = {
            'origin': f'scikit-image {skimage.__version__}, skimage.data.{source}' if source else 'synthetic, made by the script',
            'licence': SOURCES[source][2] if source else 'no licence needed: generated pixels',
            'producedBy': f'scripts/visual-references.py gif, {tools}: {how}' + (f', saved with {settings}' if settings else ''),
            'date': TODAY,
        }

    crops = {
        'astronaut': Image.fromarray(data.astronaut()[60:211, 140:343]),
        'coffee': Image.fromarray(data.coffee()[120:271, 200:403]),
        'chelsea': Image.fromarray(data.chelsea()[40:191, 100:303]),
    }
    for name, crop in crops.items():
        how = f'a 203 x 151 crop of skimage.data.{name}, reduced to 256 colours by Pillow'
        paletted = crop.quantize(256)
        save(f'{name}.gif', paletted, how, name, interlace=False)
        save(f'{name}-interlaced.gif', paletted, how, name, interlace=True)
    save('coffee-local-table.gif', crops['coffee'].quantize(64), 'the coffee crop in 64 colours', 'coffee', include_color_table=True,
         interlace=False)
    save('coffee-comment.gif', crops['coffee'].quantize(32), 'the coffee crop in 32 colours', 'coffee', comment=b'Pragmatic', interlace=False)

    # Transparency: the chelsea crop with a hard-edged transparent disc.
    rgb = np.asarray(crops['chelsea'])
    yy, xx = np.mgrid[0:rgb.shape[0], 0:rgb.shape[1]]
    alpha = np.where(np.hypot(yy - 75, xx - 100) < 40, 0, 255).astype(np.uint8)
    save('chelsea-transparent.gif', Image.fromarray(np.dstack([rgb, alpha])), 'the chelsea crop with a transparent disc', 'chelsea',
         interlace=False)

    # Few colours, so the minimum code size is 2, 2, 4 and 8.
    rng = np.random.default_rng(855)
    for colours in (2, 4, 16, 200):
        palette = rng.integers(0, 256, size=(colours, 3), dtype=np.uint8)
        index = (np.add.outer(np.arange(67) // 5, np.arange(45) // 3) + rng.integers(0, 2, size=(67, 45))) % colours
        save(f'palette-{colours}.gif', Image.fromarray(palette[index]),
             f'a 45 x 67 pattern over {colours} random colours, NumPy default_rng(855)', interlace=False)

    # The smallest images, one row and one column: under 16 pixels a side Pillow never interlaces.
    for width, height in ((1, 1), (13, 1), (1, 13), (31, 13)):
        tile = Image.fromarray(rng.integers(0, 256, size=(height, width, 3), dtype=np.uint8))
        save(f'random-{width}x{height}.gif', tile, f'{width} x {height} random pixels, NumPy default_rng(855)')

    # An animation: the first frame is the image; Pillow writes the later ones as smaller changed regions.
    base = np.asarray(crops['coffee'].quantize(128).convert('RGB'))
    frames = [Image.fromarray(base)]
    for shift in (1, 2):
        frame = base.copy()
        frame[10:60, 10:90] = np.roll(frame[10:60, 10:90], shift * 17, axis=1)
        frames.append(Image.fromarray(frame))
    save('animated.gif', frames[0], 'three frames: the coffee crop in 128 colours, then two with a shifted rectangle', 'coffee',
         save_all=True, append_images=frames[1:], duration=100, loop=0, interlace=False)
    write_manifest(GIF, entries)


LIBTIFF_RELEASE = 'https://download.osgeo.org/libtiff/tiff-4.7.1.tar.gz'
LIBTIFF_RELEASE_SHA256 = 'f698d94f3103da8ca7438d84e0344e453fe0ba3b7486e04c5bf7a9a3fabe9b69'
LIBTIFF_LICENCE = ('libtiff\'s licence, LICENSE-libtiff.md beside the files: "Permission to use, copy, modify, distribute, and sell '
                   'this software and its documentation for any purpose is hereby granted without fee"')
LIBTIFF_FILES = [
    '32bpp-None-jpeg.tiff', '32bpp-None.tiff', 'custom_dir_EXIF_GPS.tiff', 'deflate-last-strip-extra-data.tiff',
    'logluv-3c-16b.tiff', 'lzw-single-strip.tiff', 'minisblack-1c-16b.tiff', 'minisblack-1c-8b.tiff',
    'minisblack-2c-8b-alpha.tiff', 'miniswhite-1c-1b.tiff', 'ojpeg_chewey_subsamp21_multi_strip.tiff',
    'ojpeg_single_strip_no_rowsperstrip.tiff', 'ojpeg_zackthecat_subsamp22_single_strip.tiff', 'palette-1c-1b.tiff',
    'palette-1c-4b.tiff', 'palette-1c-8b.tiff', 'quad-lzw-compat.tiff', 'quad-tile.jpg.tiff', 'rgb-3c-16b.tiff',
    'rgb-3c-8b.tiff', 'test_float64_predictor2_be_lzw.tif', 'test_float64_predictor2_le_lzw.tif', 'test_ifd_loop_subifd.tif',
    'test_ifd_loop_to_first.tif', 'test_ifd_loop_to_self.tif', 'test_two_ifds.tif', 'testfax3_bug54_1dnoEOL.tif',
    'testfax3_bug_513.tiff', 'testfax4.tiff', 'tiff_with_subifd_chain.tif', 'webp_lossless_rgba_alpha_fully_opaque.tif',
]


def tiff_lzw(data):
    """TIFF's LZW: most significant bit first, a clear code first, the code width raised one code early, a new table
    before the 4,096th entry, and the end code last."""
    out, bits, held = bytearray(), 0, 0

    def put(code, width):
        nonlocal bits, held
        bits = (bits << width) | code
        held += width
        while held >= 8:
            held -= 8
            out.append((bits >> held) & 0xFF)

    table, width, prefix = {}, 9, None
    put(256, width)
    for byte in data:
        key = (prefix, byte)
        if prefix is not None and key in table:
            prefix = table[key]
            continue
        if prefix is None:
            prefix = byte
            continue
        put(prefix, width)
        table[key] = 258 + len(table)
        if 258 + len(table) + 1 > (1 << width) and width < 12:
            width += 1
        if 258 + len(table) >= 4094:
            put(256, width)
            table, width = {}, 9
        prefix = byte
    if prefix is not None:
        put(prefix, width)
        if 258 + len(table) + 1 + 1 > (1 << width) and width < 12:
            width += 1
    put(257, width)
    if held:
        out.append((bits << (8 - held)) & 0xFF)
    return bytes(out)


def packbits(data):
    """Apple's PackBits: runs of two or more equal bytes as a repeat, the rest as literals of up to 128 bytes."""
    out, i = bytearray(), 0
    while i < len(data):
        run = 1
        while i + run < len(data) and run < 128 and data[i + run] == data[i]:
            run += 1
        if run > 1:
            out += bytes([257 - run, data[i]])
            i += run
            continue
        start = i
        while i < len(data) and i - start < 128 and (i + 1 >= len(data) or data[i + 1] != data[i]):
            i += 1
        if i == start:
            i += 1
        out.append(i - start - 1)
        out += data[start:i]
    return bytes(out)


def write_tiff(path, pixels, photometric, *, compression=1, predictor=1, planar=1, tile=None, rows_per_strip=None, order='<',
               big=False, extra_samples=None, sample_format=1, bits=None, jpeg=None):
    """A TIFF from a height x width x samples array, with every layout choice in the caller's hands: strips or tiles,
    chunky or planar, either byte order, classic or BigTIFF. Partial edge tiles are padded with zeros."""
    import struct
    import zlib
    height, width, samples = pixels.shape
    bits = bits or pixels.dtype.itemsize * 8
    if bits == 1:
        packed = np.packbits(pixels[:, :, 0].astype(bool), axis=1)
    dtype = pixels.dtype.newbyteorder(order)

    def chunk_bytes(region):
        if bits == 1:
            raw = region.tobytes()
        else:
            region = region.astype(dtype)
            if predictor == 2:
                region = region.copy()
                region[:, 1:] = (region[:, 1:].astype(np.int64) - region[:, :-1].astype(np.int64)).astype(dtype)
            if predictor == 3:
                h, w, s = region.shape
                bytes_ = region.astype(pixels.dtype.newbyteorder('>')).view(np.uint8).reshape(h, w * s, pixels.dtype.itemsize)
                shuffled = bytes_.transpose(0, 2, 1).reshape(h, -1).astype(np.int16)
                shuffled[:, s:] -= shuffled[:, :-s].copy()
                raw = (shuffled & 0xFF).astype(np.uint8).tobytes()
            else:
                raw = region.tobytes()
        if compression == 1:
            return raw
        if compression == 5:
            return tiff_lzw(raw)
        if compression == 8:
            return zlib.compress(raw)
        if compression == 32773:
            row = len(raw) // region.shape[0]
            return b''.join(packbits(raw[r * row:(r + 1) * row]) for r in range(region.shape[0]))
        if compression == 7:
            return jpeg(region)
        raise ValueError(compression)

    planes = [pixels[:, :, s:s + 1] for s in range(samples)] if planar == 2 else [pixels]
    chunks = []
    for plane in planes:
        source = packed[:, :, None] if bits == 1 else plane
        if tile:
            tw, th = tile
            for ty in range(0, height, th):
                for tx in range(0, width, tw):
                    block = np.zeros((th, tw if bits != 1 else tw // 8, source.shape[2]), source.dtype)
                    part = source[ty:ty + th, (tx if bits != 1 else tx // 8):(tx + tw if bits != 1 else (tx + tw) // 8)]
                    block[:part.shape[0], :part.shape[1]] = part
                    chunks.append(chunk_bytes(block))
        else:
            step = rows_per_strip or height
            for y in range(0, height, step):
                chunks.append(chunk_bytes(source[y:y + step]))

    tags = {256: (4, [width]), 257: (4, [height]), 258: (3, [bits] * samples), 259: (3, [compression]),
            262: (3, [photometric]), 277: (3, [samples]), 284: (3, [planar]), 339: (3, [sample_format] * samples)}
    if predictor != 1:
        tags[317] = (3, [predictor])
    if extra_samples:
        tags[338] = (3, extra_samples)
    offset_type = 16 if big else 4
    if tile:
        tags[322], tags[323] = (4, [tile[0]]), (4, [tile[1]])
        offsets_tag, counts_tag = 324, 325
    else:
        tags[278] = (4, [rows_per_strip or height])
        offsets_tag, counts_tag = 273, 279

    header = 16 if big else 8
    data_start = header
    body = bytearray()
    positions = []
    for chunk in chunks:
        positions.append(data_start + len(body))
        body += chunk
        if len(body) % 2:
            body += b'\0'
    tags[offsets_tag] = (offset_type, positions)
    tags[counts_tag] = (offset_type, [len(c) for c in chunks])

    sizes = {3: 2, 4: 4, 16: 8}
    formats = {3: 'H', 4: 'I', 16: 'Q'}
    inline = 8 if big else 4
    ifd_at = data_start + len(body)
    entry_size = 20 if big else 12
    count_size = 8 if big else 2
    values_at = ifd_at + count_size + entry_size * len(tags) + (8 if big else 4)
    entries, values = bytearray(), bytearray()
    for tag in sorted(tags):
        kind, items = tags[tag]
        data = struct.pack(order + formats[kind] * len(items), *items)
        entries += struct.pack(order + 'HH', tag, kind) + struct.pack(order + ('Q' if big else 'I'), len(items))
        if len(data) <= inline:
            entries += data.ljust(inline, b'\0')
        else:
            entries += struct.pack(order + ('Q' if big else 'I'), values_at + len(values))
            values += data
            if len(values) % 2:
                values += b'\0'
    magic = b'II' if order == '<' else b'MM'
    if big:
        head = magic + struct.pack(order + 'HHHQ', 43, 8, 0, ifd_at)
        ifd = struct.pack(order + 'Q', len(tags)) + entries + struct.pack(order + 'Q', 0)
    else:
        head = magic + struct.pack(order + 'HI', 42, ifd_at)
        ifd = struct.pack(order + 'H', len(tags)) + entries + struct.pack(order + 'I', 0)
    path.write_bytes(head + body + ifd + values)


def tiff_files():
    """libtiff's test images, byte for byte, then files Pillow (through libtiff) and this script wrote: every
    compression, layout and sample type the native decoder reads, and some it refuses, for the managed decoder to be
    held to what the native one did with each."""
    TIFF.mkdir(parents=True, exist_ok=True)
    for old in list(TIFF.glob('*.tif')) + list(TIFF.glob('*.tiff')):
        old.unlink()
    entries = libtiff_files()
    entries.update(made_tiff_files())
    write_manifest(TIFF, entries)


def libtiff_files():
    """libtiff's test images from its 4.7.1 release, the libtiff Pillow builds on, copied byte for byte, with
    libtiff's licence beside them. The tarball is checked against its SHA-256 before anything is taken from it."""
    import hashlib
    import io
    import tarfile
    entries = {}
    with urllib.request.urlopen(LIBTIFF_RELEASE) as response:
        tarball = response.read()
    if hashlib.sha256(tarball).hexdigest() != LIBTIFF_RELEASE_SHA256:
        raise SystemExit(f'{LIBTIFF_RELEASE}: the SHA-256 is not the one recorded')
    with tarfile.open(fileobj=io.BytesIO(tarball), mode='r:gz') as archive:
        def member(path):
            return archive.extractfile(f'tiff-4.7.1/{path}').read()
        for name in LIBTIFF_FILES:
            (TIFF / name).write_bytes(member(f'test/images/{name}'))
            origin = f'libtiff 4.7.1, test/images/{name} in {LIBTIFF_RELEASE}'
            if name == 'deflate-last-strip-extra-data.tiff':
                origin += ' (libtiff took it from GDAL, autotest/alg/data/utmsmall_near.tiff)'
            entries[name] = {'origin': origin, 'licence': LIBTIFF_LICENCE,
                             'producedBy': 'scripts/visual-references.py tiff: copied byte for byte', 'date': TODAY}
        (TIFF / 'LICENSE-libtiff.md').write_bytes(member('LICENSE.md'))
        entries['LICENSE-libtiff.md'] = {
            'origin': f'libtiff 4.7.1, LICENSE.md in {LIBTIFF_RELEASE}', 'licence': LIBTIFF_LICENCE,
            'producedBy': 'scripts/visual-references.py tiff: copied byte for byte, the notice libtiff\'s licence asks copies to carry',
            'date': TODAY,
        }
    return entries


def made_tiff_files():
    """TIFF files Pillow (through libtiff) and this script's own writer made from scikit-image's photographs: every
    compression, layout and sample type the native decoder reads, and some it refuses."""
    import io
    entries = {}
    tools = f'Pillow {PIL.__version__} with libtiff {features.version("libtiff")}, NumPy {np.__version__}'
    coffee = data.coffee()[120:271, 200:403]
    chelsea = data.chelsea()[40:191, 100:303]

    def made(file, how, source, by):
        entries[file] = {
            'origin': f'scikit-image {skimage.__version__}, skimage.data.{source}' if source else 'synthetic, made by the script',
            'licence': SOURCES[source][2] if source else 'no licence needed: generated pixels',
            'producedBy': f'scripts/visual-references.py tiff, {by}: {how}', 'date': TODAY,
        }

    def pillow(file, image, how, source, **options):
        image.save(TIFF / file, 'TIFF', **options)
        settings = ', '.join(f'{key}={value!r}' for key, value in sorted(options.items()) if key != 'append_images')
        made(file, how + (f', saved with {settings}' if settings else ''), source, tools)

    rgb = Image.fromarray(coffee)
    for compression in ('raw', 'tiff_lzw', 'tiff_deflate', 'tiff_adobe_deflate', 'packbits', 'jpeg'):
        pillow(f'coffee-rgb-{compression}.tiff', rgb, 'the 203 x 151 coffee crop, RGB', 'coffee', compression=compression)
        pillow(f'coffee-grey-{compression}.tiff', rgb.convert('L'), 'the coffee crop in grey', 'coffee', compression=compression)
    rgba = np.dstack([chelsea, np.tile(np.linspace(0, 255, chelsea.shape[1]).astype(np.uint8), (chelsea.shape[0], 1))])
    pillow('chelsea-rgba-lzw.tiff', Image.fromarray(rgba), 'the 203 x 151 chelsea crop with a horizontal alpha ramp', 'chelsea',
           compression='tiff_lzw')
    pillow('chelsea-cmyk.tiff', Image.fromarray(chelsea).convert('CMYK'), 'the chelsea crop in CMYK', 'chelsea', compression='raw')
    pillow('chelsea-grey16.tiff', Image.fromarray(np.asarray(Image.fromarray(chelsea).convert('L')).astype(np.uint16) * 257),
           'the chelsea crop in 16-bit grey', 'chelsea')
    bilevel = Image.fromarray(coffee).convert('L').point(lambda v: 255 if v > 110 else 0).convert('1')
    for compression in ('raw', 'packbits', 'group4', 'tiff_lzw'):
        pillow(f'coffee-bilevel-{compression}.tiff', bilevel, 'the coffee crop thresholded at 110', 'coffee', compression=compression)
    pillow('coffee-two-pages.tiff', rgb, 'two pages: the coffee crop, then the chelsea crop', 'coffee', save_all=True,
           append_images=[Image.fromarray(chelsea)])
    pillow('coffee-rgb-bigtiff.tiff', rgb, 'the coffee crop as BigTIFF', 'coffee', big_tiff=True)
    pillow('coffee-rgb-strips.tiff', rgb, 'the coffee crop in strips of 16 rows, the last one short', 'coffee',
           tiffinfo={278: 16})

    def mine(file, pixels, how, source, photometric, **options):
        write_tiff(TIFF / file, pixels, photometric, **options)
        settings = ', '.join(f'{key}={value!r}' for key, value in sorted(options.items()) if key != 'jpeg')
        made(file, f'{how}, written by the script\'s own TIFF writer with {settings}', source, f'NumPy {np.__version__}')

    def jpeg_of(region):
        out = io.BytesIO()
        Image.fromarray(region).save(out, 'JPEG', quality=90)
        return out.getvalue()

    rgb8 = np.ascontiguousarray(coffee)
    for compression, label in ((1, 'raw'), (5, 'lzw'), (8, 'deflate'), (32773, 'packbits')):
        mine(f'coffee-tiles-{label}.tiff', rgb8, 'the coffee crop in 64 x 48 tiles, the edge ones padded', 'coffee', 2,
             compression=compression, tile=(64, 48))
    mine('coffee-tiles-jpeg.tiff', rgb8, 'the coffee crop in 64 x 48 tiles, each a JPEG Pillow wrote at quality 90', 'coffee', 2,
         compression=7, tile=(64, 48), jpeg=jpeg_of)
    mine('coffee-planar.tiff', rgb8, 'the coffee crop, one plane per channel, strips of 40 rows', 'coffee', 2, planar=2,
         rows_per_strip=40)
    # 144 rows, a whole number of tiles: past the first plane the TIFF crate does not take a bottom tile's padding off,
    # and on the last plane that runs past its buffer, a panic the native library does not survive.
    mine('coffee-planar-tiles-lzw.tiff', np.ascontiguousarray(rgb8[:144]), 'the top 144 rows of the coffee crop, one plane per '
         'channel, in 32 x 48 tiles, the right ones padded', 'coffee', 2, planar=2, tile=(32, 48), compression=5)
    mine('coffee-big-endian.tiff', rgb8, 'the coffee crop, big-endian', 'coffee', 2, order='>', rows_per_strip=50)
    mine('coffee-bigtiff-tiles.tiff', rgb8, 'the coffee crop as BigTIFF in tiles', 'coffee', 2, big=True, tile=(48, 48),
         compression=8)
    mine('coffee-predictor-lzw.tiff', rgb8, 'the coffee crop with the horizontal predictor', 'coffee', 2, predictor=2, compression=5)
    rgb16 = (rgb8.astype(np.uint16) * 257 + np.arange(rgb8.shape[1], dtype=np.uint16)[None, :, None]).astype(np.uint16)
    mine('coffee-rgb16-predictor.tiff', rgb16, 'the coffee crop in 16 bits, a ramp in the low byte, horizontal predictor', 'coffee',
         2, predictor=2, compression=8)
    mine('coffee-rgb16-big-endian.tiff', rgb16, 'the 16-bit coffee crop, big-endian', 'coffee', 2, order='>', rows_per_strip=37)
    alpha16 = np.tile(np.linspace(0, 65535, rgb8.shape[1]).astype(np.uint16), (rgb8.shape[0], 1))
    mine('coffee-rgba16.tiff', np.dstack([rgb16, alpha16]), 'the 16-bit coffee crop with an alpha ramp', 'coffee', 2,
         extra_samples=[2])
    mine('coffee-rgba-associated.tiff', np.dstack([rgb8, rgba[:, :, 3]]), 'the coffee crop with associated alpha', 'coffee', 2,
         extra_samples=[1])
    mine('coffee-rgb-two-extra.tiff', np.dstack([rgb8, rgba[:, :, 3], 255 - rgba[:, :, 3]]),
         'the coffee crop with two unspecified extra samples', 'coffee', 2, extra_samples=[0, 0])
    floats = (rgb8.astype(np.float32) / 255.0 * 1.25 - 0.1).astype(np.float32)
    floats[0, :3, 0] = [np.nan, np.inf, -np.inf]
    mine('coffee-float.tiff', floats, 'the coffee crop as floats from -0.1 to 1.15, with a NaN and two infinities', 'coffee', 2,
         sample_format=3)
    mine('coffee-float-predictor.tiff', floats, 'the float coffee crop with the floating-point predictor', 'coffee', 2,
         sample_format=3, predictor=3, compression=8)
    mine('coffee-float-rgba.tiff', np.dstack([floats, floats[:, :, :1]]), 'the float coffee crop with its red as alpha', 'coffee', 2,
         sample_format=3, extra_samples=[2])
    grey16 = rgb16[:, :, :1]
    mine('coffee-grey16-white-is-zero.tiff', grey16, 'the 16-bit red of the coffee crop, white is zero', 'coffee', 0)
    mine('coffee-grey8-white-is-zero.tiff', rgb8[:, :, 1:2], 'the green of the coffee crop, white is zero', 'coffee', 0,
         compression=32773)
    mine('coffee-cmyk16.tiff', np.dstack([rgb16, rgb16[:, :, :1]]), 'the 16-bit coffee crop with its red as the black channel',
         'coffee', 5)
    bits = (np.asarray(bilevel) > 0).astype(np.uint8)[:, :, None]
    mine('coffee-bilevel-tiles.tiff', bits, 'the thresholded coffee crop, one bit per pixel, in 32 x 32 tiles', 'coffee', 1,
         bits=1, tile=(32, 32))
    mine('coffee-bilevel-strips.tiff', bits, 'the thresholded coffee crop, one bit per pixel, white is zero, strips of 10',
         'coffee', 0, bits=1, rows_per_strip=10, compression=8)
    mine('coffee-grey-int8.tiff', rgb8[:, :, :1].astype(np.int8), 'the red of the coffee crop as signed samples', 'coffee', 1,
         sample_format=2)
    mine('coffee-grey4.tiff', (rgb8[:, :, :1] >> 4).astype(np.uint8), 'the red of the coffee crop as 4-bit samples, unpacked',
         'coffee', 1, bits=4)
    return entries


def ccitt_files():
    """Fax streams libtiff coded, through Pillow, for the core's CCITT decoder: Group 4, and Group 3 one and two
    dimensional, with and without fill before each end of line. Each `.fax` is a TIFF strip's bytes; the `.bits` beside
    it is the image a line at a time, a bit a pixel, most significant first, 1 for black, from the pixels Pillow coded
    (its photometric interpretation turned back into fax colours)."""
    import io
    CCITT.mkdir(parents=True, exist_ok=True)
    for old in list(CCITT.glob('*.fax')) + list(CCITT.glob('*.bits')):
        old.unlink()
    entries = {}
    tools = f'Pillow {PIL.__version__} with libtiff {features.version("libtiff")}, NumPy {np.__version__}'
    coffee = Image.fromarray(data.coffee()[120:271, 200:403]).convert('L').point(lambda v: 255 if v > 110 else 0).convert('1')
    yy, xx = np.mgrid[0:61, 0:1800]
    stripes = Image.fromarray(((xx // (1 + yy % 9) + yy) % 5 < 2)).convert('1')
    images = {'coffee': (coffee, 'the 203 x 151 coffee crop thresholded at 110', 'coffee'),
              'stripes': (stripes, '1800 x 61 stripes of changing widths, runs past the 1728 make-up code', None)}
    variants = {'g4': ('group4', None, 'Group 4'), 'g3-1d': ('group3', 0, 'Group 3 one-dimensional'),
                'g3-2d': ('group3', 1, 'Group 3 two-dimensional'), 'g3-1d-fill': ('group3', 4, 'Group 3 one-dimensional, fill before EOL'),
                'g3-2d-fill': ('group3', 5, 'Group 3 two-dimensional, fill before EOL')}
    for name, (image, how, source) in images.items():
        for variant, (compression, options, label) in variants.items():
            out = io.BytesIO()
            image.save(out, 'TIFF', compression=compression, **({'tiffinfo': {292: options}} if options is not None else {}))
            out.seek(0)
            with Image.open(out) as back:
                offsets, counts = back.tag_v2[273], back.tag_v2[279]
                if len(offsets) != 1:
                    raise SystemExit(f'{name} {variant}: {len(offsets)} strips')
                black = (np.asarray(back) != 0) if back.tag_v2[262] == 1 else (np.asarray(back) == 0)
            file = f'{name}-{variant}-{image.width}x{image.height}'
            (CCITT / f'{file}.fax').write_bytes(out.getvalue()[offsets[0]:offsets[0] + counts[0]])
            (CCITT / f'{file}.bits').write_bytes(np.packbits(black, axis=1).tobytes())
            for suffix in ('fax', 'bits'):
                entries[f'{file}.{suffix}'] = {
                    'origin': f'scikit-image {skimage.__version__}, skimage.data.{source}' if source else 'synthetic, made by the script',
                    'licence': SOURCES[source][2] if source else 'no licence needed: generated pixels',
                    'producedBy': f'scripts/visual-references.py ccitt, {tools}: {how}, '
                                  + (f'coded {label} by libtiff, the strip\'s bytes' if suffix == 'fax' else 'the pixels coded, 1 for black'),
                    'date': TODAY,
                }
    write_manifest(CCITT, entries)


COMMANDS = {'photographs': photographs, 'metrics': metric_fixtures, 'webp': webp_files, 'bmp': bmp_files, 'gif': gif_files,
            'ccitt': ccitt_files,
            'tiff': tiff_files}

if __name__ == '__main__':
    if len(sys.argv) != 2 or sys.argv[1] not in COMMANDS:
        sys.exit(f'usage: python -I scripts/visual-references.py {{{"|".join(COMMANDS)}}}')
    COMMANDS[sys.argv[1]]()
