from __future__ import annotations

import json
import runpy
from pathlib import Path

import pytest


MODULE = runpy.run_path(str(Path(__file__).resolve().parents[2] / "scripts" / "manual" / "analyze_radio_audio.py"))
RadioAudioAnalysisError = MODULE["RadioAudioAnalysisError"]
parse_ffmpeg_output = MODULE["parse_ffmpeg_output"]
parse_ffprobe_output = MODULE["parse_ffprobe_output"]
parse_silence_output = MODULE["parse_silence_output"]


def test_parse_ffprobe_output_requires_one_bounded_audio_stream() -> None:
    result = parse_ffprobe_output(
        json.dumps(
            {
                "streams": [
                    {
                        "codec_name": "mp3",
                        "sample_rate": "44100",
                        "channels": 2,
                        "channel_layout": "stereo",
                        "duration": "265.012187",
                        "bit_rate": "128000",
                    }
                ],
                "format": {"format_name": "mp3", "duration": "265.012188", "bit_rate": "128001"},
            }
        )
    )

    assert result == {
        "codec": "mp3",
        "container": "mp3",
        "sampleRateHz": 44100,
        "channels": 2,
        "channelLayout": "stereo",
        "durationSeconds": 265.012188,
        "bitRateBps": 128001,
    }


@pytest.mark.parametrize("streams", [[], [{"codec_name": "mp3"}, {"codec_name": "mp3"}]])
def test_parse_ffprobe_output_rejects_missing_or_multiple_audio_streams(streams: list[dict[str, str]]) -> None:
    with pytest.raises(RadioAudioAnalysisError, match="exactly one audio stream"):
        parse_ffprobe_output(json.dumps({"streams": streams, "format": {}}))


def test_parse_ffmpeg_output_uses_final_measurement_summaries() -> None:
    output = """
[Parsed_ebur128_0] Summary:
  Integrated loudness:
    I:         -18.2 LUFS
  Loudness range:
    LRA:         7.4 LU
  True peak:
    Peak:       -1.3 dBFS
[Parsed_volumedetect_1] n_samples: 23374080
[Parsed_volumedetect_1] mean_volume: -19.8 dB
[Parsed_volumedetect_1] max_volume: -1.5 dB
[Parsed_volumedetect_1] histogram_0db: 0
"""

    assert parse_ffmpeg_output(output) == {
        "integratedLufs": -18.2,
        "loudnessRangeLu": 7.4,
        "truePeakDbtp": -1.3,
        "decodedSampleCount": 23374080,
        "meanVolumeDbfs": -19.8,
        "samplePeakDbfs": -1.5,
        "highestBucketSampleCount": 0,
    }


def test_parse_ffmpeg_output_rejects_nonfinite_measurements() -> None:
    output = """
Integrated loudness:
  I: -inf LUFS
Loudness range:
  LRA: 0.0 LU
True peak:
  Peak: -inf dBFS
n_samples: 10
mean_volume: -inf dB
max_volume: -inf dB
"""

    with pytest.raises(RadioAudioAnalysisError, match="integrated loudness is not finite"):
        parse_ffmpeg_output(output)


def test_parse_silence_output_classifies_edges_and_internal_intervals() -> None:
    output = """
[silencedetect] silence_start: 0
[silencedetect] silence_end: 1.5 | silence_duration: 1.5
[silencedetect] silence_start: 20
[silencedetect] silence_end: 26 | silence_duration: 6
[silencedetect] silence_start: 98
[silencedetect] silence_end: 100 | silence_duration: 2
"""

    assert parse_silence_output(output, 100.0) == {
        "silenceIntervalCount": 3,
        "totalSilenceSeconds": 9.5,
        "leadingSilenceSeconds": 1.5,
        "trailingSilenceSeconds": 2.0,
        "maximumInternalSilenceSeconds": 6.0,
    }


def test_parse_silence_output_rejects_an_end_without_a_start() -> None:
    with pytest.raises(RadioAudioAnalysisError, match="end without a start"):
        parse_silence_output("silence_end: 2 | silence_duration: 2", 10.0)
