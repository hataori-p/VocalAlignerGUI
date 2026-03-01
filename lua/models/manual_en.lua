-- Manual alignment profile for English IPA (no ONNX model required)
-- Uses the same phoneme set as the English Hybrid phonemizer.
-- Assign this profile to enable elastic drag and manual alignment for English.

local model = {}

model.id           = "manual_en"
model.display_name = "Manual — English IPA"
model.model_file   = nil
model.refiner_file = nil
model.encoding     = "IPA"
model.mode         = "manual"

-- These symbols match the CMU -> IPA mapping in en_transcriber.lua
model.phoneme_set = {
    "ɑ",  "æ",  "ʌ",  "ə",  "ɔ",  "aʊ", "aɪ",
    "b",  "tʃ", "d",  "ð",  "ɛ",  "ɝ",
    "eɪ", "f",  "ɡ",  "h",  "ɪ",  "i",
    "dʒ", "k",  "l",  "m",  "n",  "ŋ",
    "oʊ", "ɔɪ", "p",  "ɹ",  "s",  "ʃ",
    "t",  "θ",  "ʊ",  "u",  "v",  "w",
    "j",  "z",  "ʒ",  "sil", "sp", "_"
}

return model
