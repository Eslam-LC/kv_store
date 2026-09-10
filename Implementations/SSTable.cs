using System.Text;
using kv_store.Enums;
using static kv_store.Implementations.BloomFilter;

namespace kv_store.Implementations
{
    public class SSTable(long entriesCount)
    {
        static readonly byte[] _magicVersion = [.. Encoding.UTF8.GetBytes("SST"), 1];
        const int FooterSize = -52;
        public string Path { get; set; } = null!;

        /*
        [magic/version: 4B]                          // header, fixed offset 0
        [record blocks: Key | ValueLen | Value]...   // your KVPairIO records, sequential
        [sparse index: Key | Offset]...              // one entry per N records, or one per block
        [BloomFilter bytes]
        [First key][Last key]
        [Footer, FIXED SIZE, written last]:
            [IndexOffset: 8B]  [IndexLength: 4B]
            [FilterOffset: 8B] [FilterLength: 4B]
            [BitSize: 8B] [HashCount: 4B]
            [First/Last Key Offset: 8B]
            [RecordCount: 4B]
            [Magic/Version: 4B]                      // repeated at the tail too — lets you validate reading backward too
        */

        byte[] MagicVersion { get; set; } = _magicVersion;
        SparseIndex SparseIndex_ { get; set; } = new();
        BloomFilter BloomFilter_ { get; set; } = new(entriesCount);
        string? FirstKey;
        string? LastKey;
        FooterTag Footer_ { get; set; } = new() { MagicVersion = _magicVersion };

        record FooterTag
        {
            public long IndexOffset { get; set; }
            public int IndexLength { get; set; }
            public long FilterOffset { get; set; }
            public int FilterLength { get; set; }
            public long BitSize { get; set; }
            public int HashCount { get; set; }
            public long FirstLastKeyOffset { get; set; }
            public int RecordCount { get; set; }
            public required byte[] MagicVersion { get; set; }

            public ErrorCode WriteTag(BinaryWriter w)
            {
                try
                {
                    w.Write(IndexOffset);
                    w.Write(IndexLength);
                    w.Write(FilterOffset);
                    w.Write(FilterLength);
                    w.Write(BitSize);
                    w.Write(HashCount);
                    w.Write(FirstLastKeyOffset);
                    w.Write(RecordCount);
                    w.Write(MagicVersion);
                }
                catch (IOException)
                {
                    return ErrorCode.IOError;
                }
                catch (ObjectDisposedException)
                {
                    return ErrorCode.UnInitializedInstance;
                }
                catch
                {
                    return ErrorCode.UnexpectedError;
                }
                return ErrorCode.None;
            }

            public ErrorCode ReadTag(BinaryReader r)
            {
                try
                {
                    IndexOffset = r.ReadInt64();
                    IndexLength = r.ReadInt32();
                    FilterOffset = r.ReadInt64();
                    FilterLength = r.ReadInt32();
                    BitSize = r.ReadInt64();
                    HashCount = r.ReadInt32();
                    FirstLastKeyOffset = r.ReadInt64();
                    RecordCount = r.ReadInt32();
                    MagicVersion = r.ReadBytes(4);
                }
                catch (IOException)
                {
                    return ErrorCode.IOError;
                }
                catch (ObjectDisposedException)
                {
                    return ErrorCode.UnInitializedInstance;
                }
                catch
                {
                    return ErrorCode.UnexpectedError;
                }
                return ErrorCode.None;
            }
        }

        public static ErrorCode WriteTableToFile(
            string path,
            in ImmutableSkipList<string, byte[]?> keyValues,
            int entriesCount,
            out ImmutableSSTable? immutableSSTable
        )
        {
            immutableSSTable = null;
            try
            {
                using FileStream stream = new(path, FileMode.Create, FileAccess.Write);
                using BinaryWriter w = new(stream);

                SSTable sst = new(entriesCount);

                ErrorCode errorCode;
                w.Write(_magicVersion);

                int i = 0;
                foreach (var entry in keyValues)
                {
                    if (i % 10 == 0)
                    {
                        errorCode = sst.SparseIndex_.AddEntry(entry.Key, w.BaseStream.Position);
                        if (errorCode != ErrorCode.None)
                            return errorCode;
                    }

                    errorCode = sst.BloomFilter_.Add(entry.Key);
                    if (errorCode != ErrorCode.None)
                        return errorCode;

                    sst.Footer_.RecordCount++;

                    errorCode = WARecord.WriteFrame(
                        w,
                        entry.Value == null ? WAOperation.DELETE : WAOperation.PUT,
                        entry.Key,
                        entry.Value!
                    );
                    if (errorCode != ErrorCode.None)
                        return errorCode;

                    if (i == 0)
                        sst.FirstKey = entry.Key;
                    if (i == entriesCount - 1)
                        sst.LastKey = entry.Key;
                    i++;
                }

                if (
                    string.IsNullOrWhiteSpace(sst.FirstKey)
                    || string.IsNullOrWhiteSpace(sst.LastKey)
                )
                    return ErrorCode.EntryIsEmpty;

                sst.Footer_.IndexOffset = w.BaseStream.Position;
                errorCode = sst.SparseIndex_.WriteIndex(w);
                if (errorCode != ErrorCode.None)
                    return errorCode;
                sst.Footer_.IndexLength = (int)(w.BaseStream.Position - sst.Footer_.IndexOffset);

                sst.Footer_.FilterOffset = w.BaseStream.Position;
                var filterBytes = sst.BloomFilter_.GetBytes;
                if (filterBytes == null)
                    return ErrorCode.UnexpectedError;
                w.Write(filterBytes);
                sst.Footer_.FilterLength = (int)(w.BaseStream.Position - sst.Footer_.FilterOffset);
                sst.Footer_.BitSize = sst.BloomFilter_.BitSize;
                sst.Footer_.HashCount = sst.BloomFilter_.HashCount;

                sst.Footer_.FirstLastKeyOffset = w.BaseStream.Position;
                w.Write(sst.FirstKey);
                w.Write(sst.LastKey);

                errorCode = sst.Footer_.WriteTag(w);
                if (errorCode != ErrorCode.None)
                    return errorCode;

                stream.Flush(true);

                sst.Path = path;
                immutableSSTable = sst.GetImmutableSSTable();
            }
            catch (IOException)
            {
                return ErrorCode.IOError;
            }
            catch (ObjectDisposedException)
            {
                return ErrorCode.UnInitializedInstance;
            }
            catch
            {
                return ErrorCode.UnexpectedError;
            }

            return ErrorCode.None;
        }

        public class ImmutableSSTable(
            string FileName,
            byte[] MagicVersion,
            ImmutableSkipList<string, long> SparseIndex,
            byte[] FilterBytes,
            long BitSize,
            int HashCount,
            string FirstKey,
            string LastKey,
            long RecordCount
        )
        {
            public string FileName_ = FileName;
            public readonly byte[] MagicVersion_ = MagicVersion;
            public readonly ImmutableSkipList<string, long> SparseIndex_ = SparseIndex;
            public readonly ImmutableBloomFilter BloomFilter_ = new(
                FilterBytes,
                BitSize,
                HashCount
            );
            public readonly string FirstKey_ = FirstKey;
            public readonly string LastKey_ = LastKey;
            public readonly long RecordCount_ = RecordCount;

            public ErrorCode TryReadEntry(string key, out byte[]? value)
            {
                value = null;
                ErrorCode errCode;
                if (
                    key.CompareTo(FirstKey_, StringComparison.Ordinal) < 0
                    || key.CompareTo(LastKey_, StringComparison.Ordinal) > 0
                )
                    return ErrorCode.KeyNotFound;

                errCode = BloomFilter_.Contains(key, out bool MayExist);
                if (errCode != ErrorCode.None)
                    return errCode;

                if (!MayExist)
                    return ErrorCode.KeyNotFound;

                if (!SparseIndex_.GetValueAtOrBefore(key, out long offset))
                    return ErrorCode.KeyNotFound;

                using FileStream stream = new(FileName_, FileMode.Open, FileAccess.Read);
                using BinaryReader reader = new(stream);

                reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                errCode = WARecord.ReadFrame(
                    reader,
                    out var op,
                    out string? fetchedKey,
                    out value!
                );
                if (value == null && op == WAOperation.PUT)
                    value = [];

                while (key.CompareTo(fetchedKey, StringComparison.Ordinal) > 0)
                {
                    errCode = WARecord.ReadFrame(reader, out op, out fetchedKey, out value!);
                    if (value == null && op == WAOperation.PUT)
                        value = [];
                    if (errCode != ErrorCode.None)
                        return errCode;
                }

                if (
                    string.IsNullOrWhiteSpace(fetchedKey)
                    || errCode != ErrorCode.None
                    || fetchedKey.CompareTo(key, StringComparison.Ordinal) != 0
                )
                    return ErrorCode.CorruptedEntry;

                if (op == WAOperation.DELETE)
                    return ErrorCode.KeyDeleted;

                return ErrorCode.None;
            }
        }

        public ImmutableSSTable GetImmutableSSTable()
        {
            return new(
                Path,
                _magicVersion,
                SparseIndex_.GetEntries(),
                BloomFilter_.GetBytes!,
                BloomFilter_.BitSize,
                BloomFilter_.HashCount,
                FirstKey!,
                LastKey!,
                Footer_.RecordCount
            );
        }

        /*
        [magic/version: 4B]                          // header, fixed offset 0
        [record blocks: Key | ValueLen | Value]...   // your KVPairIO records, sequential
        [sparse index: Key | Offset]...              // one entry per N records, or one per block
        [BloomFilter bytes]
        [First key][Last key]
        [Footer, FIXED SIZE, written last]:
            [IndexOffset: 8B]  [IndexLength: 4B]
            [FilterOffset: 8B] [FilterLength: 4B]
            [BitSize: 8B] [HashCount: 4B]
            [First/Last Key Offset: 8B]
            [RecordCount: 4B]
            [Magic/Version: 4B]                      // repeated at the tail too — lets you validate reading backward too
        */

        public static ErrorCode ReadFileToTable(string path, out ImmutableSSTable? immutableSSTable)
        {
            immutableSSTable = null;
            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read);
                using BinaryReader r = new(stream);

                if (!r.ReadBytes(4).SequenceEqual(_magicVersion))
                    return ErrorCode.FileCorruptedOrUnsupportedVersion;
                r.BaseStream.Seek(FooterSize, SeekOrigin.End);
                var Footer_ = new FooterTag() { MagicVersion = [] };
                var errCode = Footer_.ReadTag(r);
                if (errCode != ErrorCode.None)
                    return errCode;

                if (!Footer_.MagicVersion.SequenceEqual(_magicVersion))
                    return ErrorCode.FileCorruptedOrUnsupportedVersion;

                r.BaseStream.Seek(Footer_.IndexOffset, SeekOrigin.Begin);
                errCode = SparseIndex.ReadIndex(
                    r,
                    Footer_.IndexLength,
                    out ImmutableSkipList<string, long> sparseIndex
                );
                if (errCode != ErrorCode.None)
                    return errCode;

                r.BaseStream.Seek(Footer_.FilterOffset, SeekOrigin.Begin);
                byte[] filterBytes = r.ReadBytes(Footer_.FilterLength);
                if (filterBytes.Length != Footer_.FilterLength)
                    return ErrorCode.CorruptedEntry;

                r.BaseStream.Seek(Footer_.FirstLastKeyOffset, SeekOrigin.Begin);
                var firstKey = r.ReadString();
                var lastKey = r.ReadString();

                immutableSSTable = new(
                    path,
                    _magicVersion,
                    sparseIndex,
                    filterBytes,
                    Footer_.BitSize,
                    Footer_.HashCount,
                    firstKey,
                    lastKey,
                    Footer_.RecordCount
                );
            }
            catch (EndOfStreamException)
            {
                return ErrorCode.CorruptedEntry;
            }
            catch (IOException)
            {
                return ErrorCode.IOError;
            }
            catch (ObjectDisposedException)
            {
                return ErrorCode.UnInitializedInstance;
            }
            catch
            {
                return ErrorCode.UnexpectedError;
            }

            return ErrorCode.None;
        }
    }
}
