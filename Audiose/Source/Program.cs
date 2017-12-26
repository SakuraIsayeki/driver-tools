﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using System.Xml;
using System.Xml.Linq;

using DSCript;
using DSCript.Spooling;

namespace Audiose
{
    enum ParseResult
    {
        Success,
        Failure,
    }

    class Program
    {
        static readonly string DefaultOutput = "SoundData";
        static readonly string GSDName = "SOUND.GSD";

        static void Abort(int exitCode)
        {
            Console.WriteLine(">> Aborting...");
            Environment.Exit(exitCode);
        }
        
        static ParseResult ParseData()
        {
            var gsdFile = new GSDFile();
            
            if (Config.Extract)
            {
                var chunkFile = new FileChunker();

                var spoolers = new List<SpoolableBuffer>();

                chunkFile.SpoolerLoaded += (s, e) => {
                    // ignore chunks
                    if (!(s is SpoolableBuffer))
                        return;

                    var spooler = s as SpoolableBuffer;

                    switch ((ChunkType)spooler.Context)
                    {
                    case ChunkType.SpooledSoundBank:
                    case ChunkType.SpooledGameSoundBank:
                        spoolers.Add(spooler);
                        break;
                    }
                };

                chunkFile.Load(Config.Input);

                if (spoolers.Count > 0)
                {
                    var soundBanks = new List<SoundBank>();

                    foreach (var spooler in spoolers)
                    {
                        using (var ms = spooler.GetMemoryStream())
                        {
                            var id = ms.ReadInt32();

                            var gsdOffset = 0;
                            var gsdSize = 0;

                            switch (id)
                            {
                            // [VS11] - vehicle sound data
                            case 0x31315356:
                                {
                                    var vsbOffset = ms.ReadInt32();

                                    gsdOffset = ms.ReadInt32();
                                    gsdSize = ms.ReadInt32();
                                }
                                break;
                            // [SS12] - character sound data
                            case 0x32315353:
                                {
                                    // I forgot what this is
                                    ms.Position += 4;

                                    gsdOffset = ms.ReadInt32();
                                    gsdSize = ms.ReadInt32();

                                    var locOffset = ms.ReadInt32();
                                }
                                break;
                            // when in doubt, leave it out
                            default:
                                continue;
                            }
                            
                            ms.Position = gsdOffset;

                            var bank = new SoundBank();

                            var bankInfo = ms.Read<SoundBankInfo3>();
                            bankInfo.DataOffset += gsdOffset;

                            var bankDetail = (ISoundBankInfoDetail)bankInfo;

                            bankInfo.CopyTo(bank);

                            for (int s = 0; s < bank.Samples.Capacity; s++)
                            {
                                ms.Position = gsdOffset + ((s * bankDetail.SampleSize) + bankDetail.HeaderSize);

                                var sampleInfo = ms.Read<SoundSampleInfo>(bankDetail.SampleSize);
                                sampleInfo.Offset += bankDetail.DataOffset;

                                var sample = new SoundSample() {
                                    FileName = $"{s:D2}.wav",

                                    NumChannels = ((sampleInfo.Flags & bankDetail.SampleChannelFlags) != 0) ? 2 : 1,
                                    SampleRate = sampleInfo.SampleRate,

                                    Flags = (sampleInfo.Flags & ~bankDetail.SampleChannelFlags),

                                    Unknown1 = sampleInfo.Unk_0B,
                                    Unknown2 = sampleInfo.Unk_0C,
                                };
                                
                                bank.Samples.Add(sample);

                                // retrieve the buffer
                                var buffer = new byte[sampleInfo.Size];

                                ms.Position = sampleInfo.Offset;
                                ms.Read(buffer, 0, buffer.Length);
                                
                                sample.Buffer = buffer;
                            }

                            soundBanks.Add(bank);
                        }
                    }

                    if (soundBanks.Count > 0)
                    {
                        // make sure we have enough slots
                        var numBanks = 0;

                        foreach (var bank in soundBanks)
                        {
                            if (bank == null)
                                continue;

                            if (bank.Index > numBanks)
                                numBanks = bank.Index;
                        }

                        // set the correct banks so we don't use references
                        var gsdBanks = new SoundBank[numBanks + 1];
                        
                        foreach (var bank in soundBanks)
                            gsdBanks[bank.Index] = bank;
                        
                        var curRef = gsdBanks.First((b) => b != null);

                        // now make sure empty ones are created
                        for (int i = 0; i < gsdBanks.Length; i++)
                        {
                            if (gsdBanks[i] != null)
                            {
                                curRef = gsdBanks[i];
                                continue;
                            }
                            
                            var index = -1;

                            if (i < curRef.Index)
                                index = curRef.Index;
                            
                            gsdBanks[i] = new SoundBank() {
                                Index = index,
                                Samples = new List<SoundSample>(),
                            };
                        }

                        // always assume DPL's format
                        gsdFile.Type = GSDFormatType.BK31;
                        gsdFile.Banks = new List<SoundBank>(gsdBanks);

                        if (Config.Compile)
                        {
                            if (String.IsNullOrEmpty(Config.OutDir))
                                Config.OutDir = Path.GetDirectoryName(Config.Input);

                            gsdFile.SaveBinary(Path.Combine(Config.OutDir, String.Format("{0}.gsd", Path.GetFileName(Config.Input))));
                        }
                        else
                        {
                            if (String.IsNullOrEmpty(Config.OutDir))
                            {
                                Config.OutDir = Path.Combine(Path.GetDirectoryName(Config.Input), DefaultOutput,
                                    Path.GetFileNameWithoutExtension(Config.Input));
                            }

                            gsdFile.DumpAllBanks(Config.OutDir);
                        }
                    }
                    else
                    {
                        Console.WriteLine("WARNING: No sound banks were extracted.");
                    }
                }
                else
                {
                    Console.WriteLine("WARNING: No compatible formats were available for extraction.");
                }
            }
            else
            {
                switch (Config.InputType)
                {
                case FileType.BinaryData:
                    {
                        gsdFile.LoadBinary(Config.Input);

                        if (Config.Compile)
                        {
                            if (String.IsNullOrEmpty(Config.OutDir))
                                Config.OutDir = Path.GetDirectoryName(Config.Input);

                            gsdFile.SaveBinary(Path.Combine(Config.OutDir, GSDName));
                        }
                        else
                        {
                            if (String.IsNullOrEmpty(Config.OutDir))
                                Config.OutDir = Path.Combine(Path.GetDirectoryName(Config.Input), DefaultOutput);

                            gsdFile.DumpAllBanks(Config.OutDir);
                        }
                    }
                    break;
                case FileType.Xml:
                    {
                        if (Config.Extract)
                        {
                            Console.WriteLine($"ERROR: Invalid usage of 'extract' parameter, cannot use an XML file!");
                            return ParseResult.Failure;
                        }

                        gsdFile.LoadXml(Config.Input);

                        if (String.IsNullOrEmpty(Config.OutDir))
                            Config.OutDir = Path.GetDirectoryName(Config.Input);

                        gsdFile.SaveBinary(Path.Combine(Config.OutDir, GSDName));
                    }
                    break;
                default:
                    {
                        Console.WriteLine($"Couldn't determine the input file type of '{Config.Input}'!");
                        return ParseResult.Failure;
                    }
                }
            }

            return ParseResult.Success;
        }

        static ParseResult ParseDataPS1()
        {
            var ps1 = new PS1BankFile();

            if (Config.Extract)
            {
                Console.WriteLine("'Extract' argument is invalid for PS1 sound data.");
                return ParseResult.Failure;
            }

            if (Config.Compile)
            {
                Console.WriteLine("Cannot compile PS1 sound data, operation unsupported.");
                return ParseResult.Failure;
            }

            ps1.Type = (Config.InputType == FileType.Sbk) ? PS1BankType.Single : PS1BankType.Multiple;

            using (var fs = File.Open(Config.Input, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ps1.LoadBinary(fs);

                if (String.IsNullOrEmpty(Config.OutDir))
                    Config.OutDir = Path.Combine(Path.GetDirectoryName(Config.Input), $"{Path.GetFileNameWithoutExtension(Config.Input)}_Data");

                ps1.DumpAllBanks(Config.OutDir);
            }

            return ParseResult.Success;
        }
        
        static unsafe ParseResult ParseStuntman()
        {
            if (Config.Extract)
            {
                Console.WriteLine("'Extract' argument is invalid for Stuntman sound data.");
                return ParseResult.Failure;
            }

            if (Config.Compile)
            {
                Console.WriteLine("Cannot compile Stuntman sound data, operation unsupported.");
                return ParseResult.Failure;
            }

            if (String.IsNullOrEmpty(Config.OutDir))
                Config.OutDir = Path.Combine(Path.GetDirectoryName(Config.Input), $"{Path.GetFileNameWithoutExtension(Config.Input)}_Data");

            var xmlVerbose = false;

            using (var fs = File.Open(Config.Input, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                switch (Config.InputType)
                {
                case FileType.Blk:
                    {
                        int blockOffset = -1;
                        int blockIndex = -1;

                        int numBlocks = 0;
                        
                        var nextBlock = new Func<Stream, bool>((stream) => {
                            if ((blockOffset == -1) && (blockIndex == -1))
                            {
                                blockOffset = (int)stream.Position;
                                numBlocks = (stream.ReadInt32() >> 2) - 1;

                                // no blocks!
                                if (numBlocks == 0)
                                    return false;
                            }
                            
                            if (++blockIndex < numBlocks)
                            {
                                stream.Position = blockOffset + (blockIndex * 4);
                                stream.Position = (stream.ReadInt32() + blockOffset);
                                return true;
                            }

                            blockIndex = -1;
                            return false;
                        });
                        
                        var blkXml = new XmlDocument();
                        var blkRoot = blkXml.CreateElement("StuntmanSoundDatabase");

                        blkRoot.SetAttribute("Name", Path.GetFileNameWithoutExtension(Config.Input));

                        while (nextBlock(fs))
                        {
                            var bankOffset = (int)fs.Position;

                            var bankCmt = blkXml.CreateComment($"offset: {bankOffset:X}");
                            blkRoot.AppendChild(bankCmt);

                            var bankXml = blkXml.CreateElement("Bank");

                            bankXml.SetAttribute("Index", $"{blockIndex}");

                            var bankSize = fs.ReadInt32();

                            var extraDataOffset = fs.ReadInt32() + bankOffset;
                            var extraDataSize = (bankSize - extraDataOffset);

                            if (extraDataSize > 0)
                            {
                                var bankCmt2 = blkXml.CreateComment($"extra data @ {extraDataOffset:X} (size: {extraDataSize:X})");
                                blkRoot.AppendChild(bankCmt2);
                            }

                            // this was VERY sneaky -- it definitely threw me off!
                            var numSounds = (int)fs.ReadUInt16();

                            var listOffset = (int)fs.Position;

                            var dataOffset = listOffset + (numSounds * 0xC);
                            var dataSize = (extraDataOffset - dataOffset);

                            var dataSizeAlign = Memory.Align(dataSize, 4);
                            
                            var bufSize = 0;
                            
                            for (int i = 0; i < numSounds; i++)
                            {
                                fs.Position = listOffset + (i * 0xC);

                                var offset = (int)fs.ReadUInt16();
                                var oPage = (int)fs.ReadUInt16(); // which page (per 65535 byte-boundary)
                                var size = (int)fs.ReadUInt16();
                                var sPage = (int)fs.ReadUInt16();
                                var flags = (int)fs.ReadUInt16();
                                var freq = (int)fs.ReadUInt16();

                                // this was a pretty smart way to throw people off...lol
                                offset += (oPage * 65536);
                                size += (sPage * 65536);

                                // verify the BLK stuff
                                bufSize += size;
                                
                                // retrieve the buffer
                                var buffer = new byte[size];

                                fs.Position = (dataOffset + offset);
                                fs.Read(buffer, 0, buffer.Length);

                                var soundData = VAG.DecodeSound(buffer);

                                // export the data
                                var soundName = $"{blockIndex:D2}_{i:D2}.wav";
                                var soundPath = Path.Combine("Sounds", soundName);
                                
                                var outFile = Path.Combine(Config.OutDir, soundPath);

                                // setup the directory structure
                                var soundDir = Path.GetDirectoryName(outFile);

                                if (!Directory.Exists(soundDir))
                                    Directory.CreateDirectory(soundDir);

                                // build XML
                                if (xmlVerbose)
                                {
                                    var soundCmt = blkXml.CreateComment($"offset: {offset:X}({oPage}), size: {size:X}({sPage})");
                                    bankXml.AppendChild(soundCmt);
                                }

                                var soundXml = blkXml.CreateElement("Sample");
                                
                                soundXml.SetAttribute("Index", $"{i}");
                                soundXml.SetAttribute("Flags", $"{flags:X}");
                                soundXml.SetAttribute("Freq", $"{freq}");
                                soundXml.SetAttribute("File", soundPath);
                                
                                bankXml.AppendChild(soundXml);
                                
                                using (var soundFile = File.Open(outFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                                {
                                    var chunk = new AudioFormatChunk(1, freq);
                                    soundFile.WriteRIFF(soundData, chunk);
                                }
                            }
                            
                            if (bufSize != dataSizeAlign)
                                throw new InvalidOperationException($"BUFFER SIZE MISMATCH! ({bufSize:X} != {dataSizeAlign:X})");
                            
                            blkRoot.AppendChild(bankXml);
                        }
                        
                        blkXml.AppendChild(blkRoot);
                        blkXml.Save(Path.Combine(Config.OutDir, "config.xml"));
                    } break;
                case FileType.Xav:
                    {
                        fs.Seek(0, SeekOrigin.End);
                        var length = (int)fs.Position;

                        fs.Seek(0, SeekOrigin.Begin);

                        MagicNumber XAVHeader = "XAVS";

                        var magic = fs.ReadInt32();

                        if (magic != XAVHeader)
                        {
                            Console.WriteLine("I don't know what to do with this XAV file!");
                            return ParseResult.Failure;
                        }

                        var width = (int)fs.ReadInt16();
                        var height = (int)fs.ReadInt16();

                        var frames = fs.ReadInt32();
                        var audstr = (int)fs.ReadInt16();
                        var type = (int)fs.ReadInt16();
                        var v_chsz = fs.ReadInt32();
                        var a_chsz = fs.ReadInt32();

                        Console.WriteLine($" XAV file: {width}x{height}, {frames} frames");
                        Console.WriteLine($"  audio streams: {audstr}");
                        Console.WriteLine($"  type: {type}");
                        Console.WriteLine($"  v_chsz: {v_chsz}");
                        Console.WriteLine($"  a_chsz: {a_chsz}");

                        if (Config.HasArg("info"))
                        {
                            Console.WriteLine("> Information dumped successfully.");
                            return ParseResult.Success;
                        }

                        if (audstr == 0)
                        {
                            Console.WriteLine($"WARNING: XAV doesn't contain any audio data!");
                            return ParseResult.Failure;
                        }

                        var audsrc = 0;

                        // check if user specified an audio source that exists
                        if (Config.GetArg("audsrc", ref audsrc) || Config.GetArg("aud", ref audsrc))
                        {
                            if (audsrc == 0)
                            {
                                Console.WriteLine("WARNING: Audio stream index is not zero-based, but I'll assume you meant '1' ;)");
                            }
                            else
                            {
                                audsrc -= 1;
                            }
                        }
                        else
                        {
                            Console.WriteLine("> No audio stream index provided, using default");
                        }

                        var audidx = (audsrc + 1);

                        if (audsrc > audstr)
                        {
                            Console.WriteLine($"WARNING: XAV only contains {audstr} audio streams -- stream {audidx} does not exist.");
                            return ParseResult.Failure;
                        }
                        
                        Console.WriteLine($"> Loading data from audio stream {audidx}...");
                        
                        byte cmd = 0;
                        int chunk = 0;

                        var read_chunk = new Func<bool>(() => {
                            MagicNumber EOSIdent = "_EOS";
                            
                            var value = fs.ReadInt32();

                            if (value == EOSIdent)
                                return false;

                            cmd = (byte)(value & 0xFF);
                            chunk = ((value >> 8) & 0xFFFFFF);

                            return true;
                        });

                        var get_byte = new Func<int, int>((idx) => {
                            return ((chunk >> (idx * 8)) & 0xFF);
                        });

                        var chunks = new int[32][];
                        var nChunks = 0;

                        for (int i = 0; i < 32; i++)
                            chunks[i] = new int[2] { -1, -1 };
                        
                        var blockPtrs = new int[2][];
                        var blockCounts = new int[2];
                        
                        for (int i = 0; i < 2; i++)
                            blockPtrs[i] = new int[0x23FFF];
                        
                        var store_block = new Action<int, int>((index, count) => {
                            var block = blockCounts[index]++;

                            blockPtrs[index][block] = (int)fs.Position;
                            fs.Position += count;
                        });

                        var skip_block = new Action<int>((count) => {
                            fs.Position += count;
                        });

                        var store_chunk = new Action(() => {
                            chunks[nChunks++] = new[] { blockCounts[0], blockCounts[1] };
                        });

                        var skip_chunk = new Action(() => {
                            ++nChunks;
                        });
                        
                        while (read_chunk())
                        {
                            if (cmd == '!')
                            {
                                var v0 = get_byte(0);

                                if (v0 == '_')
                                {
                                    v0 = get_byte(1);

                                    if (v0 == (audsrc + 'A') || v0 == (audsrc + 'a'))
                                    {
                                        v0 = get_byte(2);

                                        if (v0 == '0')
                                        {
                                            Console.WriteLine($">> EOS @ {fs.Position:X8}");
                                            break;
                                        }
                                    }
                                }
                                // ???
                                else if (v0 == 'u')
                                {
                                    store_chunk();
                                }
                                else if (v0 == '$')
                                {
                                    // video-related?
                                    if (get_byte(1) == 'V')
                                    {
                                        store_chunk();
                                    }
                                }
                                else
                                {
                                    skip_chunk();
                                }
                            }
                            // audio track
                            else if(cmd == (audsrc + 'A') || cmd == (audsrc + 'a'))
                            {
                                store_block(0, chunk);
                            }
                            // video track
                            else if (cmd == 'V')
                            {
                                store_block(1, chunk);
                            }
                            else
                            {
                                skip_block(chunk);
                            }
                        }
                        
                        var nBlocksAudio = blockCounts[0];
                        var nBlocksVideo = blockCounts[1];
                        
                        if (nBlocksAudio > 0)
                        {
                            Console.WriteLine("> Processing audio data...");

                            var buffer = new byte[nBlocksAudio * a_chsz];
                            var bufPtr = 0;

                            var blockSize = (a_chsz >> 1);
                            var channelSize = (blockSize >> 1);

                            for (int i = 0; i < nBlocksAudio; i++)
                            {
                                var block = new byte[blockSize];

                                fs.Position = blockPtrs[0][i];
                                fs.Read(block, 0, blockSize);

                                fixed (byte* b = block)
                                fixed (byte* r = buffer)
                                {
                                    var idx = 0;

                                    for (int k = 0; k < (channelSize >> 1); k += 2)
                                    {
                                        var offset = bufPtr + (idx * 2);

                                        var ch_l = *(short*)(b + k);
                                        var ch_r = *(short*)(b + k + channelSize);

                                        *(short*)(r + offset) = ch_l;
                                        *(short*)(r + offset + 2) = ch_l;
                                        *(short*)(r + offset + 4) = ch_l;
                                        *(short*)(r + offset + 6) = ch_l;

                                        idx += 4;
                                    }

                                    idx = 0;

                                    for (int k = 0; k < (channelSize >> 1); k += 2)
                                    {
                                        var offset = bufPtr + (idx * 2) + blockSize;

                                        var ch_l = *(short*)(b + k);
                                        var ch_r = *(short*)(b + k + channelSize);

                                        *(short*)(r + offset) = ch_r;
                                        *(short*)(r + offset + 2) = ch_r;
                                        *(short*)(r + offset + 4) = ch_r;
                                        *(short*)(r + offset + 6) = ch_r;

                                        idx += 4;
                                    }
                                }

                                bufPtr += a_chsz;
                            }

                            var dumpName = $"{Path.GetFileNameWithoutExtension(Config.Input)}_{audidx:D2}.wav";
                            var dumpPath = Path.Combine(Path.GetDirectoryName(Config.Input), dumpName);

                            Console.WriteLine($">> Saving to '{dumpPath}'...");

                            using (var f = File.Open(dumpPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                            {
                                var fmtChunk = new AudioFormatChunk(2, 48000);
                                f.WriteRIFF(buffer, fmtChunk);
                            }
                        }

                        if ((nBlocksVideo > 0) && Config.HasArg("woah"))
                        {
                            Console.WriteLine($"> WOAH!!! {nBlocksAudio} audio / {nBlocksVideo} video blocks and {nChunks} chunks.");
                            Console.WriteLine("> Dumping video data...");

                            var dumpName = $"{Path.GetFileNameWithoutExtension(Config.Input)}_dump.bin";
                            var dumpPath = Path.Combine(Path.GetDirectoryName(Config.Input), dumpName);

                            using (var f = File.Open(dumpPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                            {
                                f.Write((int)((MagicNumber)"WOAH"));

                                f.Write((short)width);
                                f.Write((short)height);

                                f.Write(frames);

                                f.Write((short)audstr);
                                f.Write((short)type);

                                f.Write(v_chsz);
                                f.Write(a_chsz);

                                f.Write(nBlocksAudio);
                                f.Write(nBlocksVideo);

                                var tA = 0;
                                var tV = 0;

                                for (int i = 0; i < chunks.Length; i++)
                                {
                                    var chk = chunks[i];

                                    if (chk == null)
                                        break;
                                    if ((chk[0] == -1) && (chk[1] == -1))
                                        continue;

                                    var nA = chk[0];
                                    var nV = chk[1];

                                    f.Write(i);
                                    f.Write(nA - tA);
                                    f.Write(nV - tV);
                                    
                                    tA = nA;
                                    tV = nV;
                                }

                                f.Write((int)MagicNumber.FIREBIRD);
                                
                                var buffer = new byte[nBlocksVideo * v_chsz];
                                var bufPtr = 0;

                                for (int i = 0; i < nBlocksVideo; i++)
                                {
                                    fs.Position = (blockPtrs[1][i] - 4);
                                    var size = ((fs.ReadInt32() >> 8) & 0xFFFFFF);

                                    var block = new byte[size];
                                    fs.Read(block, 0, size);

                                    Buffer.BlockCopy(block, 0, buffer, bufPtr, size);
                                    bufPtr += v_chsz;
                                }

                                f.Write((int)((MagicNumber)"vvvv"));
                                f.Write(buffer.Length);
                                f.Write(buffer, 0, buffer.Length);

                                f.Write((int)((MagicNumber)"xxxx"));
                            }
                        }
                    } break;
                default:
                    Console.WriteLine("Sorry, that format is not supported.");
                    return ParseResult.Failure;
                }
            }

            return ParseResult.Success;
        }

        static ParseResult ParseXA()
        {
            if (Config.Extract)
            {
                Console.WriteLine("'Extract' argument is invalid for XA audio data.");
                return ParseResult.Failure;
            }

            if (Config.Compile)
            {
                Console.WriteLine("Cannot compile XA audio data, operation unsupported.");
                return ParseResult.Failure;
            }

            var xaName = Path.GetFileNameWithoutExtension(Config.Input);

            using (var fs = File.Open(Config.Input, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                MagicNumber XAHeader_v1 = 0x92783465; // ' e4x’ '
                MagicNumber XAHeader_v2 = "XA30";

                var magic = fs.ReadInt32();
                var version = 0;

                if (magic == XAHeader_v1)
                    version = 1;
                if (magic == XAHeader_v2)
                    version = 2;
                
                var listOffset = 0;
                var platform = -1; // PS2 = 0, Xbox = 1, PC = 2
                
                var offsets = new int[2];
                var sizes = new int[2];

                var readList = new Action<Stream>((s) => {
                    fs.Position = listOffset;

                    for (int i = 0; i < 2; i++)
                        offsets[i] = fs.ReadInt32();
                    for (int i = 0; i < 2; i++)
                        sizes[i] = fs.ReadInt32();
                });

                var readData = new Func<Stream, int, byte[]>((s, index) => {
                    var result = new byte[sizes[index]];

                    fs.Position = offsets[index];
                    fs.Read(result, 0, result.Length);

                    return result;
                });

                if (version == 0)
                {
                    Console.WriteLine("Sorry, cannot process this file.");
                    return ParseResult.Failure;
                }

                var check = fs.ReadInt32();

                // PS2
                if (check == 22050)
                {
                    listOffset = 12;
                    platform = 0;
                }
                // Xbox
                else if (check == 2048)
                {
                    listOffset = 4;
                    platform = 1;
                }
                // PC
                else if (check == 2)
                {
                    listOffset = 16;
                    platform = 2;
                }
                else
                {
                    Console.WriteLine("WARNING: Unknown XA audio file format, please report this!");
                    return ParseResult.Failure;
                }

                if (platform == 1)
                {
                    Console.WriteLine("Sorry, Xbox audio is currently not supported.");
                    return ParseResult.Failure;
                }

                var numTracks = 0;
                var numChannels = 0;
                var frequency = 0;
                var compressed = false;

                if (version == 1)
                    numTracks = 2;
                if (version == 2)
                    numTracks = 1;

                switch (platform)
                {
                case 0:
                    {
                        numChannels = 1;
                        frequency = check;
                    }
                    break;
                case 1:
                    {
                        // TODO
                    }
                    break;
                case 2:
                    {
                        numChannels = 2;
                        frequency = fs.ReadInt32();
                        compressed = (fs.ReadInt32() == 1);
                    }
                    break;
                }

                // DPL PC music
                if (compressed)
                {
                    Console.WriteLine("Sorry, compressed audio files are currently unsupported.");
                    return ParseResult.Failure;
                }

                readList(fs);

                for (int i = 0; i < numTracks; i++)
                {
                    Console.WriteLine($"Processing audio track {i + 1} / {numTracks}...");

                    var buffer = readData(fs, i);

                    var audName = $"{xaName}_{(i + 1):D2}.wav";
                    var audPath = Path.Combine(Path.GetDirectoryName(Config.Input), audName);

                    if (platform == 0)
                        buffer = VAG.DecodeSound(buffer);

                    Console.WriteLine($"> Saving to '{audPath}'...");

                    using (var f = File.Open(audPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        var fmtChunk = new AudioFormatChunk(numChannels, frequency);
                        f.WriteRIFF(buffer, fmtChunk);
                    }
                }
            }

            return ParseResult.Success;
        }

        static ParseResult Parse()
        {
            if (Config.HasArg("stuntman"))
                return ParseStuntman();

            switch (Config.InputType)
            {
            case FileType.Blk:
            case FileType.Sbk:
                return ParseDataPS1();
            case FileType.Xa:
                return ParseXA();
            }
            
            return ParseData();
        }

        static void Run()
        {
            switch (Parse())
            {
            case ParseResult.Success:
                Console.WriteLine(Config.GetSuccessMessage());
                break;
            case ParseResult.Failure:
                Abort(2);
                break;
            }
        }

        static void Initialize()
        {
            // make sure the user's culture won't F$#% up anything!
            Thread.CurrentThread.CurrentCulture = Config.Culture;
            Thread.CurrentThread.CurrentUICulture = Config.Culture;

            Console.WriteLine($"Audiose Sound Editor ({Config.VersionString})");
        }

        static void Main(string[] args)
        {
            if (Debugger.IsAttached)
            {
                Initialize();

                Config.ProcessArgs(args);
                Run();
            }
            else
            {
                try
                {
                    if (args.Length > 0)
                    {
                        if (Config.ProcessArgs(args))
                        {
                            Run();
                        }
                        else
                        {
                            Abort(1);
                        }
                    }
                    else
                    {
                        Console.WriteLine(Config.UsageString);
                        Environment.Exit(0);
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"ERROR: {e.Message}\r\n");

                    Console.Error.WriteLine("==================== Stack Trace ====================");
                    Console.Error.WriteLine($"<{e.GetType().FullName}>:");
                    Console.Error.WriteLine(e.StackTrace);
                }
            }
        }
    }
}
