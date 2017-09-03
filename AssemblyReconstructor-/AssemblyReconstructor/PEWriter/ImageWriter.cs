using System;
using System.IO;

#if !READ_ONLY

using Mono.Cecil.Metadata;
using Mono.Cecil.PE;

using RVA = System.UInt32;

namespace PEWriter {

	sealed class ImageWriter : BinaryStreamWriter {

		public DataBuffer methodcode;
		public DataBuffer Tables;
		public DataBuffer Strings;
		public DataBuffer Blob;
		public DataBuffer US;

		public ResourceBuffer resources;
		public DataBuffer Data;
		public DataBuffer StrongName;

		readonly TextMap text_map;
		public Metadata_ReaderWriter.MetadataReader mr;
		//ImageDebugDirectory debug_directory;
		//byte [] debug_data;

		ByteBuffer win32_resources;

		const uint pe_header_size = 0x178u;
		const uint section_header_size = 0x28u;
		const uint file_alignment = 0x200;
		const uint section_alignment = 0x2000;
		const ulong image_base = 0x00400000;

		internal const RVA text_rva = 0x2000;

		readonly bool pe64;
		readonly uint time_stamp;

		internal Section text;
		internal Section rsrc;
		internal Section reloc;

		ushort sections;

		ImageWriter (Metadata_ReaderWriter.MetadataReader mr, Stream stream)
			: base (stream)
		{
			this.mr = mr;
				
			this.methodcode = new DataBuffer();
			this.methodcode.WriteBytes(mr.methodcode.ToArray());
			
			this.resources = mr.resources;
			
			this.Data = new DataBuffer();
			this.Data.WriteBytes(mr.FieldsInitialData.ToArray());
			
			this.Tables = new DataBuffer();
			this.Tables.WriteBytes(mr.TablesBytes);	
				
			this.Strings = new DataBuffer();
			this.Strings.WriteBytes(mr.Strings);	
			
			this.Blob = new DataBuffer();
			this.Blob.WriteBytes(mr.Blob);
			
			this.US = new DataBuffer();
			if (mr.US!=null)
			this.US.WriteBytes(mr.US);
			
			this.StrongName = new DataBuffer();
			if (mr.StrongName!=null)
			this.StrongName.WriteBytes(mr.StrongName);
			
			//this.pe64 = module.Architecture != TargetArchitecture.I386;
			this.pe64 = mr.inh.ifh.Machine != 0x14C;
			//this.GetDebugHeader (); removed
			this.GetWin32Resources ();
			this.text_map = BuildTextMap ();
			this.sections = (ushort) (pe64 ? 1 : 2); // text + reloc
			this.time_stamp = (uint) DateTime.UtcNow.Subtract (new DateTime (1970, 1, 1)).TotalSeconds;
		}

		void GetWin32Resources ()
		{
			byte[] raw_resources = mr.rsrcsection;
			if (raw_resources == null)
				return;
			win32_resources = new ByteBuffer (raw_resources);
		}

		public static ImageWriter CreateWriter (Metadata_ReaderWriter.MetadataReader mr, Stream stream)
		{
			var writer = new ImageWriter (mr, stream);
			writer.BuildSections ();
			return writer;
		}

		void BuildSections ()
		{
			bool has_win32_resources=true;
			if (win32_resources==null||win32_resources.length == 0)
			has_win32_resources=false;
			
			if (has_win32_resources)
				sections++;

			text = CreateSection (".text", text_map.GetLength (), null);
			var previous = text;

			if (has_win32_resources) {
				rsrc = CreateSection (".rsrc", (uint) win32_resources.length, previous);

				PatchWin32Resources (win32_resources);
				previous = rsrc;
			}

			if (!pe64)
				reloc = CreateSection (".reloc", 12u, previous);
		}

		Section CreateSection (string name, uint size, Section previous)
		{
			return new Section {
				Name = name,
				VirtualAddress = previous != null
					? previous.VirtualAddress + Align (previous.VirtualSize, section_alignment)
					: text_rva,
				VirtualSize = size,
				PointerToRawData = previous != null
					? previous.PointerToRawData + previous.SizeOfRawData
					: Align (GetHeaderSize (), file_alignment),
				SizeOfRawData = Align (size, file_alignment)
			};
		}

		public static uint Align (uint value, uint align)
		{
			align--;
			return (value + align) & ~align;
		}

		void WriteDOSHeader ()
		{
			Write (new byte [] {
				// dos header start
				0x4d, 0x5a, 0x90, 0x00, 0x03, 0x00, 0x00,
				0x00, 0x04, 0x00, 0x00, 0x00, 0xff, 0xff,
				0x00, 0x00, 0xb8, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				// lfanew
				0x80, 0x00, 0x00, 0x00,
				// dos header end
				0x0e, 0x1f, 0xba, 0x0e, 0x00, 0xb4, 0x09,
				0xcd, 0x21, 0xb8, 0x01, 0x4c, 0xcd, 0x21,
				0x54, 0x68, 0x69, 0x73, 0x20, 0x70, 0x72,
				0x6f, 0x67, 0x72, 0x61, 0x6d, 0x20, 0x63,
				0x61, 0x6e, 0x6e, 0x6f, 0x74, 0x20, 0x62,
				0x65, 0x20, 0x72, 0x75, 0x6e, 0x20, 0x69,
				0x6e, 0x20, 0x44, 0x4f, 0x53, 0x20, 0x6d,
				0x6f, 0x64, 0x65, 0x2e, 0x0d, 0x0d, 0x0a,
				0x24, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00
			});
		}

		void WritePEFileHeader ()
		{
			WriteUInt32 (0x00004550);		  // Magic
			WriteUInt16 (mr.inh.ifh.Machine); // Machine
			WriteUInt16 (sections);			  // NumberOfSections
			WriteUInt32 (time_stamp);
			WriteUInt32 (0);	// PointerToSymbolTable
			WriteUInt32 (0);	// NumberOfSymbols
			WriteUInt16 ((ushort) (!pe64 ? 0xe0 : 0xf0)); // SizeOfOptionalHeader
			WriteUInt16 (mr.inh.ifh.Characteristics);	// Characteristics
		}


		Section LastSection ()
		{
			if (reloc != null)
				return reloc;

			if (rsrc != null)
				return rsrc;

			return text;
		}

		void WriteOptionalHeaders ()
		{
			WriteUInt16 ((ushort) (!pe64 ? 0x10b : 0x20b));	// Magic
			WriteByte (8);	// LMajor
			WriteByte (0);	// LMinor
			WriteUInt32 (text.SizeOfRawData);	// CodeSize
			WriteUInt32 ((reloc != null ? reloc.SizeOfRawData : 0)
				+ (rsrc != null ? rsrc.SizeOfRawData : 0));	// InitializedDataSize
			WriteUInt32 (0);	// UninitializedDataSize

			var startub_stub = text_map.GetRange (TextSegment.StartupStub);
			WriteUInt32 (startub_stub.Length > 0 ? startub_stub.Start : 0);  // EntryPointRVA
			WriteUInt32 (text_rva);	// BaseOfCode

			if (!pe64) {
				WriteUInt32 (0);	// BaseOfData
				WriteUInt32 ((uint) image_base);	// ImageBase
			} else {
				WriteUInt64 (image_base);	// ImageBase
			}

			WriteUInt32 (section_alignment);	// SectionAlignment
			WriteUInt32 (file_alignment);		// FileAlignment

			WriteUInt16 (4);	// OSMajor
			WriteUInt16 (0);	// OSMinor
			WriteUInt16 (0);	// UserMajor
			WriteUInt16 (0);	// UserMinor
			WriteUInt16 (4);	// SubSysMajor
			WriteUInt16 (0);	// SubSysMinor
			WriteUInt32 (0);	// Reserved

			var last_section = LastSection();
			WriteUInt32 (last_section.VirtualAddress + Align (last_section.VirtualSize, section_alignment));	// ImageSize
			WriteUInt32 (text.PointerToRawData);	// HeaderSize

			WriteUInt32 (0);	// Checksum
			WriteUInt16 (mr.inh.ioh.Subsystem);	// SubSystem
			WriteUInt16 (0x8540);	// DLLFlags

			const ulong stack_reserve = 0x100000;
			const ulong stack_commit = 0x1000;
			const ulong heap_reserve = 0x100000;
			const ulong heap_commit = 0x1000;

			if (!pe64) {
				WriteUInt32 ((uint) stack_reserve);
				WriteUInt32 ((uint) stack_commit);
				WriteUInt32 ((uint) heap_reserve);
				WriteUInt32 ((uint) heap_commit);
			} else {
				WriteUInt64 (stack_reserve);
				WriteUInt64 (stack_commit);
				WriteUInt64 (heap_reserve);
				WriteUInt64 (heap_commit);
			}

			WriteUInt32 (0);	// LoaderFlags
			WriteUInt32 (16);	// NumberOfDataDir

			WriteZeroDataDirectory ();	// ExportTable
			WriteDataDirectory (text_map.GetDataDirectory (TextSegment.ImportDirectory));	// ImportTable
			if (rsrc != null) {							// ResourceTable
				WriteUInt32 (rsrc.VirtualAddress);
				WriteUInt32 (rsrc.VirtualSize);
			} else
				WriteZeroDataDirectory ();

			WriteZeroDataDirectory ();	// ExceptionTable
			WriteZeroDataDirectory ();	// CertificateTable
			WriteUInt32 (reloc != null ? reloc.VirtualAddress : 0);			// BaseRelocationTable
			WriteUInt32 (reloc != null ? reloc.VirtualSize : 0);

			WriteZeroDataDirectory ();

			WriteZeroDataDirectory ();	// Copyright
			WriteZeroDataDirectory ();	// GlobalPtr
			WriteZeroDataDirectory ();	// TLSTable
			WriteZeroDataDirectory ();	// LoadConfigTable
			WriteZeroDataDirectory ();	// BoundImport
			WriteDataDirectory (text_map.GetDataDirectory (TextSegment.ImportAddressTable));	// IAT
			WriteZeroDataDirectory ();	// DelayImportDesc
			WriteDataDirectory (text_map.GetDataDirectory (TextSegment.CLIHeader));	// CLIHeader
			WriteZeroDataDirectory ();	// Reserved
		}

		void WriteZeroDataDirectory ()
		{
			WriteUInt32 (0);
			WriteUInt32 (0);
		}


		void WriteSectionHeaders ()
		{
			WriteSection (text, 0x60000020);

			if (rsrc != null)
				WriteSection (rsrc, 0x40000040);

			if (reloc != null)
				WriteSection (reloc, 0x42000040);
		}

		void WriteSection (Section section, uint characteristics)
		{
			var name = new byte [8];
			var sect_name = section.Name;
			for (int i = 0; i < sect_name.Length; i++)
				name [i] = (byte) sect_name [i];

			WriteBytes (name);
			WriteUInt32 (section.VirtualSize);
			WriteUInt32 (section.VirtualAddress);
			WriteUInt32 (section.SizeOfRawData);
			WriteUInt32 (section.PointerToRawData);
			WriteUInt32 (0);	// PointerToRelocations
			WriteUInt32 (0);	// PointerToLineNumbers
			WriteUInt16 (0);	// NumberOfRelocations
			WriteUInt16 (0);	// NumberOfLineNumbers
			WriteUInt32 (characteristics);
		}

		void MoveTo (uint pointer)
		{
			BaseStream.Seek (pointer, SeekOrigin.Begin);
		}

		void MoveToRVA (Section section, RVA rva)
		{
			BaseStream.Seek (section.PointerToRawData + rva - section.VirtualAddress, SeekOrigin.Begin);
		}

		void MoveToRVA (TextSegment segment)
		{
			MoveToRVA (text, text_map.GetRVA (segment));
		}

		void WriteRVA (RVA rva)
		{
			if (!pe64)
				WriteUInt32 (rva);
			else
				WriteUInt64 (rva);
		}

		void WriteText ()
		{
			MoveTo (text.PointerToRawData);

			// ImportAddressTable

			if (!pe64) {
				WriteRVA (text_map.GetRVA (TextSegment.ImportHintNameTable));
				WriteRVA (0);
			}

			// CLIHeader

			WriteUInt32 (0x48);
			WriteUInt16 (2);
			WriteUInt16 ((ushort) (mr.netdir.MinorRuntimeVersion));

			WriteUInt32 (text_map.GetRVA (TextSegment.MetadataHeader));
			WriteUInt32 (GetMetadataLength ());
			WriteUInt32 ((uint)mr.netdir.Flags);
			WriteUInt32 ((uint)mr.netdir.EntryPointToken);
			WriteDataDirectory (text_map.GetDataDirectory (TextSegment.Resources));
			WriteDataDirectory (text_map.GetDataDirectory (TextSegment.StrongNameSignature));
			WriteZeroDataDirectory ();	// CodeManagerTable
			WriteZeroDataDirectory ();	// VTableFixups
			WriteZeroDataDirectory ();	// ExportAddressTableJumps
			WriteZeroDataDirectory ();	// ManagedNativeHeader

			// Code

			MoveToRVA (TextSegment.Code);
			//WriteBuffer (metadata.code);
			WriteBuffer (methodcode);

			// Resources

			MoveToRVA (TextSegment.Resources);
			WriteBuffer (this.resources);

			// Data

			if (this.Data.length > 0) {
				MoveToRVA (TextSegment.Data);
				WriteBuffer (this.Data);
			}

			// StrongNameSignature
			MoveToRVA (TextSegment.StrongNameSignature);
			WriteBuffer (this.StrongName);
			
			
			// MetadataHeader
			MoveToRVA (TextSegment.MetadataHeader);
			WriteMetadataHeader ();

			WriteMetadata ();
			
			if (pe64)
				return;

			// ImportDirectory
			MoveToRVA (TextSegment.ImportDirectory);
			WriteImportDirectory ();

			// StartupStub
			MoveToRVA (TextSegment.StartupStub);
			WriteStartupStub ();
		}

		uint GetMetadataLength ()
		{
			return text_map.GetRVA (TextSegment.DebugDirectory) - text_map.GetRVA (TextSegment.MetadataHeader);
		}

		void WriteMetadataHeader ()
		{
			WriteUInt32 (0x424a5342);	// Signature
			WriteUInt16 (1);	// MajorVersion
			WriteUInt16 (1);	// MinorVersion
			WriteUInt32 (0);	// Reserved

			WriteUInt32 ((uint) mr.mh.VersionLenght);
			WriteBytes (mr.mh.VersionString);
			WriteUInt16 (0);	// Flags
			WriteUInt16 (GetStreamCount ());

			uint offset = text_map.GetRVA (TextSegment.TableHeap) - text_map.GetRVA (TextSegment.MetadataHeader);

			WriteStreamHeader (ref offset, TextSegment.TableHeap,mr.MetadataRoot.Name);
			WriteStreamHeader (ref offset, TextSegment.StringHeap, "#Strings");
			WriteStreamHeader (ref offset, TextSegment.UserStringHeap, "#US");
			WriteStreamHeader (ref offset, TextSegment.GuidHeap, "#GUID");
			WriteStreamHeader (ref offset, TextSegment.BlobHeap, "#Blob");
		}


		ushort GetStreamCount ()
		{
			return (ushort) (
				1	// #~
				+ 1	// #Strings
				+ (US.length==0 ? 0 : 1)	 // #US
				+ 1	// GUID
				+ (Blob.length==0 ? 0 : 1)); // #Blob
		}

		void WriteStreamHeader (ref uint offset, TextSegment heap, string name)
		{
			var length = (uint) text_map.GetLength (heap);
			if (length == 0)
				return;

			WriteUInt32 (offset);
			WriteUInt32 (length);
			WriteBytes (GetZeroTerminatedString (name));
			offset += length;
		}

		static byte [] GetZeroTerminatedString (string @string)
		{
			return GetString (@string, (@string.Length + 1 + 3) & ~3);
		}

		static byte [] GetSimpleString (string @string)
		{
			return GetString (@string, @string.Length);
		}

		static byte [] GetString (string @string, int length)
		{
			var bytes = new byte [length];
			for (int i = 0; i < @string.Length; i++)
				bytes [i] = (byte) @string [i];

			return bytes;
		}

		void WriteMetadata ()
		{
			WriteHeap (TextSegment.TableHeap, this.Tables);
			WriteHeap (TextSegment.StringHeap, this.Strings);
			WriteHeap (TextSegment.UserStringHeap, this.US);
			WriteGuidHeap ();
			WriteHeap (TextSegment.BlobHeap, this.Blob);
		}


		void WriteHeap (TextSegment heap, HeapBuffer buffer)
		{
			if (buffer.IsEmpty)
				return;

			MoveToRVA (heap);
			WriteBuffer (buffer);
		}
		
		void WriteHeap (TextSegment heap, DataBuffer buffer)
		{
			MoveToRVA (heap);
			WriteBuffer (buffer);
		}
		void WriteGuidHeap ()
		{
			MoveToRVA (TextSegment.GuidHeap);
			WriteBytes (mr.GUID);
		}



		void WriteImportDirectory ()
		{
			WriteUInt32 (text_map.GetRVA (TextSegment.ImportDirectory) + 40);	// ImportLookupTable
			WriteUInt32 (0);	// DateTimeStamp
			WriteUInt32 (0);	// ForwarderChain
			WriteUInt32 (text_map.GetRVA (TextSegment.ImportHintNameTable) + 14);
			WriteUInt32 (text_map.GetRVA (TextSegment.ImportAddressTable));
			Advance (20);

			// ImportLookupTable
			WriteUInt32 (text_map.GetRVA (TextSegment.ImportHintNameTable));

			// ImportHintNameTable
			MoveToRVA (TextSegment.ImportHintNameTable);

			WriteUInt16 (0);	// Hint
			WriteBytes (GetRuntimeMain ());
			WriteByte (0);
			WriteBytes (GetSimpleString ("mscoree.dll"));
			WriteUInt16 (0);
		}

		byte [] GetRuntimeMain ()
		{
			int characteristics = (int)mr.inh.ifh.Characteristics;
			characteristics=characteristics&8192;  // file is a dll flag
			
			return (characteristics==8192)
				? GetSimpleString ("_CorDllMain")
				: GetSimpleString ("_CorExeMain");
		}

		void WriteStartupStub ()
		{
			switch (mr.inh.ifh.Machine) {
			case 0x014c:
				WriteUInt16 (0x25ff);
				WriteUInt32 ((uint) image_base + text_map.GetRVA (TextSegment.ImportAddressTable));
				return;
			default:
				throw new NotSupportedException ();
			}
		}

		void WriteRsrc ()
		{
			MoveTo (rsrc.PointerToRawData);
			WriteBuffer (win32_resources);
		}

		void WriteReloc ()
		{
			MoveTo (reloc.PointerToRawData);

			var reloc_rva = text_map.GetRVA (TextSegment.StartupStub);
			reloc_rva += mr.inh.ifh.Machine == 0x0200 ? 0x20u : 2;  // is IA64
			var page_rva = reloc_rva & ~0xfffu;

			WriteUInt32 (page_rva);	// PageRVA
			WriteUInt32 (0x000c);	// Block Size

			switch (mr.inh.ifh.Machine) {
			case 0x014c:
				WriteUInt32 (0x3000 + reloc_rva - page_rva);
				break;
			default:
				throw new NotSupportedException();
			}

			WriteBytes (new byte [file_alignment - reloc.VirtualSize]);
		}

		public void WriteImage ()
		{
			WriteDOSHeader ();
			WritePEFileHeader ();
			WriteOptionalHeaders ();
			WriteSectionHeaders ();
			WriteText ();
			if (rsrc != null)
				WriteRsrc ();
			if (reloc != null)
				WriteReloc ();
		}

		TextMap BuildTextMap ()
		{		
			var map = new TextMap ();
			map.AddMap (TextSegment.ImportAddressTable, mr.inh.ifh.Machine == 0x014c ? 8 : 0);  // I386
			map.AddMap (TextSegment.CLIHeader, 0x48, 8);
			
			//map.AddMap (TextSegment.Code, metadata.code.length, !pe64 ? 4 : 16);
			map.AddMap (TextSegment.Code, methodcode.length, !pe64 ? 4 : 16);
			map.AddMap (TextSegment.Resources, this.resources.length, 8);
			map.AddMap (TextSegment.Data, this.Data.length, 4);
			//if (metadata.data.length > 0)
			//	metadata.table_heap.FixupData (map.GetRVA (TextSegment.Data));
			
			map.AddMap (TextSegment.StrongNameSignature, this.StrongName.length, 4);

			map.AddMap (TextSegment.MetadataHeader, GetMetadataHeaderLength ());
			map.AddMap (TextSegment.TableHeap, this.Tables.length, 4);
			map.AddMap (TextSegment.StringHeap, this.Strings.length, 4);
			map.AddMap (TextSegment.UserStringHeap, this.US.length, 4);
			map.AddMap (TextSegment.GuidHeap, 16);
			map.AddMap (TextSegment.BlobHeap, this.Blob.length, 4);

			// debug_dir_len = 0
			map.AddMap (TextSegment.DebugDirectory, 0, 4);

			
			if (pe64) {
				var start = map.GetNextRVA (TextSegment.DebugDirectory);
				map.AddMap (TextSegment.ImportDirectory, new Range (start, 0));
				map.AddMap (TextSegment.ImportHintNameTable, new Range (start, 0));
				map.AddMap (TextSegment.StartupStub, new Range (start, 0));
				return map;
			}

			RVA import_dir_rva = map.GetNextRVA (TextSegment.DebugDirectory);
			RVA import_hnt_rva = import_dir_rva + 48u;
			import_hnt_rva = (import_hnt_rva + 15u) & ~15u;
			uint import_dir_len = (import_hnt_rva - import_dir_rva) + 27u;

			RVA startup_stub_rva = import_dir_rva + import_dir_len;
			startup_stub_rva = mr.inh.ifh.Machine == 0x0200  // IA64
				? (startup_stub_rva + 15u) & ~15u
				: 2 + ((startup_stub_rva + 3u) & ~3u);

			map.AddMap (TextSegment.ImportDirectory, new Range (import_dir_rva, import_dir_len));
			map.AddMap (TextSegment.ImportHintNameTable, new Range (import_hnt_rva, 0));
			map.AddMap (TextSegment.StartupStub, new Range (startup_stub_rva, GetStartupStubLength ()));

			return map;
		}

		uint GetStartupStubLength ()
		{
			switch (mr.inh.ifh.Machine) {
			case 0x014c:  // I386
				return 6;
			default:
				throw new NotSupportedException ();
			}
		}

		int GetMetadataHeaderLength ()
		{
			return
				// MetadataHeader
				40
				// #~ header
				+ 12
				// #Strings header
				+ 20
				// #US header
				+ (US.length==0 ? 0 : 12)
				// #GUID header
				+ 16
				// #Blob header
				+ (Blob.length==0 ? 0 : 16);
		}



		public DataDirectory GetStrongNameSignatureDirectory ()
		{
			return text_map.GetDataDirectory (TextSegment.StrongNameSignature);
		}

		public uint GetHeaderSize ()
		{
			return pe_header_size + (sections * section_header_size);
		}

		void PatchWin32Resources (ByteBuffer resources)
		{
			PatchResourceDirectoryTable (resources);
		}

		void PatchResourceDirectoryTable (ByteBuffer resources)
		{
			resources.Advance (12);

			var entries = resources.ReadUInt16 () + resources.ReadUInt16 ();

			for (int i = 0; i < entries; i++)
				PatchResourceDirectoryEntry (resources);
		}

		void PatchResourceDirectoryEntry (ByteBuffer resources)
		{
			resources.Advance (4);
			var child = resources.ReadUInt32 ();

			var position = resources.position;
			resources.position = (int) child & 0x7fffffff;

			if ((child & 0x80000000) != 0)
				PatchResourceDirectoryTable (resources);
			else
				PatchResourceDataEntry (resources);

			resources.position = position;
		}

		void PatchResourceDataEntry (ByteBuffer resources)
		{
			var rva = resources.ReadUInt32 ();
			resources.position -= 4;
			resources.WriteUInt32 (rva - (uint)mr.inh.ioh.ResourceDirectory.RVA + rsrc.VirtualAddress);
		}
	}
}

#endif
