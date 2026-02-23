-- Manual alignment profile for Japanese IPA (no ONNX model required)
-- Uses the same phoneme set as rex_model but operates in elastic/manual mode.
-- Assign this profile to enable elastic drag and approximate alignment
-- without loading any neural network model.

local model = {}

model.id           = "rex_manual"
model.display_name = "Manual — Japanese IPA"
model.model_file   = nil
model.refiner_file = nil
model.encoding     = "IPA"
model.mode         = "manual"

model.phoneme_set = {
    "a", "i", "ɯ", "e", "o",
    "k", "ɡ", "t", "d", "p", "b",
    "s", "z", "ɕ", "h", "ɸ",
    "m", "n", "ɴ",
    "j", "ɾ", "w",
    "v", "ʔ",
    "tɕ", "ts", "dʑ",
    "kʲ", "ɡʲ", "ç", "bʲ", "pʲ", "mʲ", "nʲ", "ɾʲ",
    "br", "sil", "sp", "_"
}

return model
