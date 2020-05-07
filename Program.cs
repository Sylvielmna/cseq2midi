using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using MiscUtil.IO;
using MiscUtil.Conversion;

namespace CSEQ2MID
{
    class Program
    {
        static void Main(string[] args)
        {
            string filenameIn = "";
            string filenameOut = "";
            EndianBinaryReader ebr;
            EndianBinaryWriter ebw;

            bool extract = false;
            int i = 0;
            while (i < args.Length)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "-e":
                        extract = true;
                        break;
                    default:
                        //Filename operation
                        string filespec = arg;
                        string specdir = Path.GetDirectoryName(filespec);
                        string specpart = Path.GetFileName(filespec);

                        // GetFiles will vomit on an empty specdir
                        bool empty = true;
                        if (specdir.Length > 2)
                        {
                            if (specdir[1] == ':')
                                empty = false;
                            else
                                empty = true;
                        }
                        else
                            empty = false;
                        if (empty)
                            return;
                        if (specdir.Length == 0)
                        {
                            specdir = Environment.CurrentDirectory;
                        }
                        filenameIn = Path.Combine(specdir, specpart);
                        filenameOut = Path.ChangeExtension(filenameIn, ".mid");
                        break;
                }
                i++;
            }

            //Start
            if (extract)
            {
                filenameOut = Path.ChangeExtension(filenameIn, ".cseq");
                ebr = new EndianBinaryReader(EndianBitConverter.Little, new FileStream(filenameIn, FileMode.Open));
                ebw = new EndianBinaryWriter(EndianBitConverter.Big, new FileStream(filenameOut, FileMode.Create));
                EXE2CSEQ(ebr, ebw);
            }
            else
            {
                ebr = new EndianBinaryReader(EndianBitConverter.Little, new FileStream(filenameIn, FileMode.Open));
                ebw = new EndianBinaryWriter(EndianBitConverter.Big, new FileStream(filenameOut, FileMode.Create));
                CSEQ2MID(ebr, ebw);
            }

        }
        static void CSEQ2MID(EndianBinaryReader ebr, EndianBinaryWriter ebw)
        {
            //Getting around
            uint filesize;
            int chunkCCount;
            int chunk8Count;
            int chunkPostCode;
            int musicTrackCount;
            uint seek;
            uint bpm;
            uint tpqn;
            int[] musicTrackOffset;
            int tracksStart;
            byte[] midiTrackData;


            filesize = ebr.ReadUInt32();
            chunkCCount = ebr.ReadByte();
            chunk8Count = ebr.ReadByte();
            chunkPostCode = ebr.ReadByte();//null track
            ebr.ReadByte();//0?
            for (int i = 0; i < chunkCCount; i++)
                ebr.ReadBytes(12);
            for (int i = 0; i < chunk8Count; i++)
                ebr.ReadBytes(8);
            switch (chunkPostCode)
            {
                case 1:
                    ebr.ReadBytes(5);
                    break;
                case 3:
                    ebr.ReadBytes(9);
                    break;
                default:
                    throw new Exception(chunkPostCode.ToString() + " not recognized!");
            }
            //general music info
            musicTrackCount = ebr.ReadByte();
            bpm = ebr.ReadUInt16();
            tpqn = ebr.ReadUInt16();

            //Write MThd header
            ebw.Write(new byte[4] { (byte)'M', (byte)'T', (byte)'h', (byte)'d' });//MThd
            ebw.Write((UInt32)6);
            ebw.Write((UInt16)1);
            ebw.Write((UInt16)musicTrackCount);
            ebw.Write((UInt16)tpqn);

            //offsets
            musicTrackOffset = new int[musicTrackCount];
            for (int i = 0; i < musicTrackCount; i++)
                musicTrackOffset[i] = ebr.ReadUInt16();

            //Music tracks
            seek = 0;
            while (seek != 9 && seek != 1)
                seek = ebr.ReadByte();
            tracksStart = (int)ebr.BaseStream.Position - 1;
            for (int i = 0; i < musicTrackCount; i++)
            {
                ebw.Write(new byte[4] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' });//MTrk
                uint mpqn;
                int channel;
                uint deltaTime;
                int opcode;
                int midiOpCode;
                int pitch;
                int velocity;
                ebr.BaseStream.Position = tracksStart + musicTrackOffset[i];
                seek = 0;
                while (seek != 9 && seek != 1)
                    seek = ebr.ReadByte();
                channel = ebr.ReadByte();
                channel = 0;

                //gather events
                using (MemoryStream memory = new MemoryStream())
                {
                    using (EndianBinaryWriter ebwTrack = new EndianBinaryWriter(EndianBitConverter.Big, memory))
                    {
                        if (i == 0)//first track put time signature, tempo
                        {

                            //Tempo
                            ebwTrack.Write((byte)0x00);//delta time
                            ebwTrack.Write((byte)0xFF);//meta event
                            ebwTrack.Write((byte)0x51);//event type
                            ebwTrack.Write((byte)0x03);//length
                            mpqn = (uint)(60000000 / bpm);
                            ebwTrack.Write((byte)(mpqn >> 16));
                            ebwTrack.Write((UInt16)(mpqn & (0xFFFF)));

                            //Time Signature
                            ebwTrack.Write((byte)0x00);//delta time
                            ebwTrack.Write((byte)0xFF);//meta event
                            ebwTrack.Write((byte)0x58);//event type
                            ebwTrack.Write((byte)0x04);//length
                            ebwTrack.Write((byte)4);//numer
                            ebwTrack.Write((byte)2);//denom
                            ebwTrack.Write((byte)24);//metro
                            ebwTrack.Write((byte)8);//32nds

                            //MIDI reset
                            ebwTrack.Write((byte)0x00);//delta time
                            ebwTrack.Write((byte)0xF0);//meta event
                            ebwTrack.Write((byte)0x05);
                            ebwTrack.Write((byte)0x7E);
                            ebwTrack.Write((byte)0x7F);
                            ebwTrack.Write((byte)0x09);
                            ebwTrack.Write((byte)0x01);
                            ebwTrack.Write((byte)0xF7);
                        }
                        //Go through original events
                        while (true)
                        {
                            deltaTime = r_var_len(ebr);
                            opcode = ebr.ReadByte();

                            switch (opcode)
                            {
                                case 5://noteOn
                                    midiOpCode = (byte)(0x90 | channel);
                                    pitch = ebr.ReadByte();
                                    if (pitch == 0x2E)
                                        pitch = 0x2E;
                                    velocity = ebr.ReadByte();
                                    ebwTrack.Write(g_var_len((uint)deltaTime));
                                    ebwTrack.Write((byte)midiOpCode);
                                    ebwTrack.Write((byte)pitch);
                                    ebwTrack.Write((byte)velocity);
                                    break;
                                case 1://noteOff
                                    midiOpCode = (byte)(0x80 | channel);
                                    pitch = ebr.ReadByte();
                                    ebwTrack.Write(g_var_len((uint)deltaTime));
                                    ebwTrack.Write((byte)midiOpCode);
                                    ebwTrack.Write((byte)pitch);
                                    ebwTrack.Write((byte)0x00);
                                    break;
                                case 3://end track
                                    midiOpCode = (byte)0xFF;
                                    ebwTrack.Write(g_var_len((uint)deltaTime));
                                    ebwTrack.Write((byte)midiOpCode);
                                    ebwTrack.Write((byte)0x2F);
                                    ebwTrack.Write((byte)0x00);
                                    goto Finish;
                                case 6://pitch bend??
                                case 7://definitely modulation
                                case 9:
                                case 2://pan?
                                    midiOpCode = (byte)(0xB0 | channel);
                                    velocity = ebr.ReadByte();
                                    ebwTrack.Write(g_var_len((uint)deltaTime));
                                    ebwTrack.Write((byte)midiOpCode);
                                    ebwTrack.Write((byte)0x01);//modulation
                                    ebwTrack.Write((byte)velocity);
                                    break;
                                default:
                                    throw new Exception(opcode.ToString() + " not recognized at " + ebr.BaseStream.Position.ToString());
                            }//end switch
                        }//end while
                    Finish:
                        ;
                    }//end using ebrTrack
                    midiTrackData = memory.ToArray();
                }//end using memory
                ebw.Write((UInt32)(midiTrackData.Length));
                ebw.Write(midiTrackData);
            }//end for
            ebr.Close();
            ebw.Close();
        }
        public static byte[] g_var_len(uint value)
        {
            if (value == 0)
                return new byte[1] { 0 };
            if (value == 1)
                return new byte[1] { 1 };
            int bitCount = (int)Math.Ceiling(Math.Log(value, 2));
            int byteCount = (int)Math.Ceiling(bitCount / 7d);
            byte[] b = new byte[byteCount];
            for (int i = byteCount - 1; i >= 0; i--)
            {
                byte byteValue = (byte)(0x7Fu & (value >> i * 7));//Shift over and truncate
                if (i == 0)
                    byteValue = g_flag_rest1(false, byteValue);
                else
                    byteValue = g_flag_rest1(true, byteValue);
                b[byteCount - 1 - i] = byteValue;//Reverse the order...
            }
            return b;
        }
        public static byte g_flag_rest1(bool flag, uint value)
        {
            byte b = (byte)(0x7Fu & value);
            if (flag)
                b = (byte)(0x80 | b);
            return b;
        }
        public static uint r_var_len(EndianBinaryReader ebr)
        {
            //Collect into list
            List<byte> bl = new List<byte>();
            byte b;
            do
            {
                b = (byte)r_byte1_be(ebr);
                bl.Add(b);
            } while (b_flag(b));

            //Determine value
            uint value = 0;
            for (int i = bl.Count() - 1; i >= 0; i--)
                value += (b_rest(bl[i]) << 7 * (bl.Count - 1 - i));
            return value;
        }
        public static uint r_byte1_be(EndianBinaryReader ebr)
        {
            return ebr.ReadByte();
        }
        public static bool b_flag(uint b)
        { return Convert.ToBoolean((byte)((b >> 7) & 0xFDu)); }
        public static uint b_rest(uint b)
        { return (uint)(b & (byte)0x7Fu); }
        //Debug/test functions
        static void EXE2CSEQ(EndianBinaryReader ebr, EndianBinaryWriter ebw)
        {
            ebr.BaseStream.Position = 0xA00u;
            uint fileLength = ebr.ReadUInt32();
            ebr.BaseStream.Position -= 4;
            for (uint i = 0; i < fileLength; i++)
                ebw.Write(ebr.ReadByte());
            ebr.Close();
            ebw.Close();

        }
    }
}
