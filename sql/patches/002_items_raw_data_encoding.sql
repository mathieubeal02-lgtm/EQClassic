-- items.raw_data holds a packed Item_Struct (292 bytes). In the public eqclassic_db dump the blobs
-- went through a latin1 -> UTF-8 conversion before being dumped: bytes >= 0x80 became 2-byte UTF-8
-- sequences (blob lengths 289..330 instead of 292), and bytes that were not valid in the source
-- charset were replaced by U+FFFD (lost for good).
--
-- Undo the conversion where it gives back exactly 292 bytes (~98% of rows). Bytes that had been
-- replaced by U+FFFD come back as '?' (0x3F), so the repaired blobs are usable but not exact.
-- The servers load items_axclassic first (clean column data, 24k of the 27k items) and only use
-- these blobs for the ~3k items that are missing from it (see Database::LoadAxclassicItems).
UPDATE items
SET raw_data = CONVERT(CONVERT(raw_data USING utf8mb4) USING latin1)
WHERE LENGTH(raw_data) <> 292
  AND LENGTH(CONVERT(CONVERT(raw_data USING utf8mb4) USING latin1)) = 292;
