-- Model profile for Allosaurus-REX (Japanese IPA alignment model)
-- This file defines:
--   1. The exact phoneme inventory the model was trained on (= the validator)
--   2. Metadata for model discovery and UI display
--
-- The phoneme_set here is the SINGLE SOURCE OF TRUTH for:
--   - ValidateGrid (highlight invalid phonemes in red)
--   - Converter compatibility scoring (✓ / ~ / ✗ in dropdown)
--   - Aligner rejection (C# will reject intervals not in this set)

local model = {}

model.id           = "rex_model"
model.display_name = "Allosaurus REX (Japanese)"
model.model_file   = "resources/models/rex_model.onnx"
model.refiner_file = "resources/models/rex_refiner.onnx"
model.encoding     = "IPA"

-- Stable IPA phoneme set — matches REX model training vocabulary exactly.
-- Source: allosaurus_rex/phonemizer/implementations/japanese/tables.py
-- STABLE_IPA_VOWELS + STABLE_IPA_CONSONANTS + special tokens
model.phoneme_set = {
    -- Vowels
    "a", "i", "ɯ", "e", "o",

    -- Stops
    "k", "ɡ", "t", "d", "p", "b",

    -- Fricatives
    "s", "z", "ɕ", "h", "ɸ",

    -- Nasals
    "m", "n", "ɴ",

    -- Approximants / liquids
    "j", "ɾ", "w",

    -- Glottal / other
    "v", "ʔ",

    -- Affricates
    "tɕ", "ts", "dʑ",

    -- Palatalized consonants
    "kʲ", "ɡʲ", "ç", "bʲ", "pʲ", "mʲ", "nʲ", "ɾʲ",

    -- Special / boundary tokens
    "br", "sil", "sp", "_"
}

-- Optional future extensions (not used by C# yet):
-- function model.describe(sym) ... end
-- function model.suggest(sym) ... end
-- function model.validate_sequence(seq) ... end

return model
