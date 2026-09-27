using System.IO;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class VoicePackTests
{
    [Fact]
    public void Select_UsesTheToneBucketAndFallsBack()
    {
        var pack = LoadSamplePack();

        Assert.Equal("dry", pack.Select("dry_sharp").Id);
        Assert.Equal("playful", pack.Select("curious").Id);
        Assert.Equal("gentle", pack.Select("not-a-tone").Id);
        Assert.Equal("gentle", pack.Select("dry_warm").Id);
        Assert.Equal("gentle", pack.Select(null).Id);
    }

    [Fact]
    public void Select_UsesLengthNightAndQuestionInsideTheTone()
    {
        var pack = LoadSizedPack();
        var shortLine = "先吃饭。";
        var longLine = "这件事你先把来龙去脉自己过一遍，想清楚再动手，我在旁边等你把这一步做完，然后再告诉我结果。";
        var question = "这个报错你看懂了吗？";

        Assert.NotEqual("long", pack.Select("calm", shortLine).Id);
        Assert.True(pack.Select("calm", shortLine).Seconds <= 11.5);
        Assert.Equal("long", pack.Select("calm", longLine).Id);
        Assert.Equal("ask", pack.Select("calm", question).Id);
        Assert.Equal("night", pack.Select("calm", shortLine, "LateNight").Id);
        Assert.Equal("short", pack.Select("calm", shortLine, preferId: "short").Id);
        Assert.NotEqual("long", pack.Select("calm", shortLine, preferId: "long").Id);
        Assert.Equal(pack.Select("calm", shortLine).Id, pack.Select("calm", shortLine).Id);
    }

    [Fact]
    public void JiayiLibrary_KeepsAtLeastFiftyReferencesFromSevenSecondsUp()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? pack = null;
        for (var depth = 0; depth < 8 && directory is not null; depth++)
        {
            var candidate = Path.Combine(directory.FullName, "voice", "packs", "jiayi");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                pack = candidate;
                break;
            }

            directory = directory.Parent;
        }

        Assert.NotNull(pack);
        var loaded = JiayiVoicePack.Load(pack);
        Assert.True(loaded.References.Count >= 50, $"references: {loaded.References.Count}");
        Assert.All(loaded.References, reference =>
        {
            Assert.True(reference.Seconds >= 7, reference.Id);
            Assert.True(File.Exists(reference.AudioPath), reference.Id);
            Assert.False(string.IsNullOrWhiteSpace(reference.Prompt));
        });
        Assert.Contains(loaded.References, reference => reference.Seconds > 10);
        foreach (var tone in new[]
                 {
                     "calm", "gentle", "playful", "dry", "dry_sharp", "serious",
                     "sleepy", "nostalgic", "curious", "intimate", "encouraging"
                 })
        {
            Assert.Contains(loaded.References, reference => reference.Tones.Contains(tone));
        }
    }

    [Fact]
    public void TryOpen_ReturnsNullWhenTheRuntimeFileIsAbsent()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pet-voice-" + Guid.NewGuid().ToString("N")));
        try
        {
            Assert.Null(VoiceRuntime.TryLoad(directory.FullName));
            Assert.Null(JiayiVoiceSpeaker.TryCreate(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void TryOpen_FindsABundledRuntimeAboveTheStartDirectory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pet-voice-" + Guid.NewGuid().ToString("N")));
        var nested = root.CreateSubdirectory(Path.Combine("bin", "app"));
        var voice = root.CreateSubdirectory("voice");
        var pythonDir = voice.CreateSubdirectory("python");
        var pretrained = voice.CreateSubdirectory(Path.Combine("engine", "GPT_SoVITS", "pretrained_models"));
        pretrained.CreateSubdirectory("chinese-roberta-wwm-ext-large");
        pretrained.CreateSubdirectory("chinese-hubert-base");
        var speakerDir = pretrained.CreateSubdirectory("sv");
        var python = Path.Combine(pythonDir.FullName, "python.exe");
        File.WriteAllText(python, string.Empty);
        File.WriteAllText(Path.Combine(speakerDir.FullName, "pretrained_eres2netv2w24s4ep4.ckpt"), string.Empty);

        try
        {
            var runtime = VoiceRuntime.TryLoad(nested.FullName);
            Assert.NotNull(runtime);
            Assert.Equal(root.FullName, runtime.VoiceRoot);
            Assert.Equal(python, runtime.PythonPath);
            Assert.Equal(Path.Combine(voice.FullName, "engine"), runtime.RootPath);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static JiayiVoicePack LoadSamplePack()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pet-pack-" + Guid.NewGuid().ToString("N")));
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "weights"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "refs"));
            File.WriteAllText(Path.Combine(root.FullName, "weights", "gpt.ckpt"), string.Empty);
            File.WriteAllText(Path.Combine(root.FullName, "weights", "sovits.pth"), string.Empty);
            File.WriteAllText(Path.Combine(root.FullName, "refs", "gentle.wav"), string.Empty);
            File.WriteAllText(Path.Combine(root.FullName, "refs", "dry.wav"), string.Empty);
            File.WriteAllText(Path.Combine(root.FullName, "refs", "playful.wav"), string.Empty);
            File.WriteAllText(
                Path.Combine(root.FullName, "manifest.json"),
                """
                {
                  "id": "jiayi",
                  "textLang": "zh",
                  "gpt": "weights/gpt.ckpt",
                  "sovits": "weights/sovits.pth",
                  "refs": [
                    {"id": "gentle", "tones": ["gentle"], "audio": "refs/gentle.wav", "prompt": "轻", "promptLang": "zh", "fallback": true},
                    {"id": "dry", "tones": ["dry", "dry_sharp"], "audio": "refs/dry.wav", "prompt": "干", "promptLang": "zh"},
                    {"id": "playful", "tones": ["playful", "curious"], "audio": "refs/playful.wav", "prompt": "活", "promptLang": "zh"}
                  ]
                }
                """);
            return JiayiVoicePack.Load(root.FullName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static JiayiVoicePack LoadSizedPack()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pet-pack-" + Guid.NewGuid().ToString("N")));
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "weights"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "refs"));
            File.WriteAllText(Path.Combine(root.FullName, "weights", "gpt.ckpt"), string.Empty);
            File.WriteAllText(Path.Combine(root.FullName, "weights", "sovits.pth"), string.Empty);
            foreach (var name in new[] { "short", "long", "ask", "night" })
            {
                File.WriteAllText(Path.Combine(root.FullName, "refs", name + ".wav"), string.Empty);
            }

            File.WriteAllText(
                Path.Combine(root.FullName, "manifest.json"),
                """
                {
                  "id": "jiayi",
                  "textLang": "zh",
                  "gpt": "weights/gpt.ckpt",
                  "sovits": "weights/sovits.pth",
                  "refs": [
                    {"id": "short", "tones": ["calm"], "situations": ["explain"], "seconds": 8.0, "audio": "refs/short.wav", "prompt": "短", "promptLang": "zh", "fallback": true},
                    {"id": "long", "tones": ["calm"], "situations": ["explain"], "seconds": 17.0, "audio": "refs/long.wav", "prompt": "长", "promptLang": "zh"},
                    {"id": "ask", "tones": ["calm"], "situations": ["question"], "seconds": 9.0, "audio": "refs/ask.wav", "prompt": "问", "promptLang": "zh"},
                    {"id": "night", "tones": ["calm"], "situations": ["night"], "seconds": 8.5, "audio": "refs/night.wav", "prompt": "夜", "promptLang": "zh"}
                  ]
                }
                """);
            return JiayiVoicePack.Load(root.FullName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
