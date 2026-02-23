-- ja_transcoder.lua
-- Japanese Transcoder (Encoding Converter) for VocalAligner
-- Converts a sequence of phoneme symbols (Romaji OR mixed IPA) to Stable IPA.
-- Port of: allosaurus_rex/phonemizer/implementations/japanese/transcoder.py
--
-- IMPORTANT: This profile operates on one phoneme per interval.
-- The C# adapter must collect all interval phonemes into a flat sequence,
-- call transcode_sequence(), then redistribute results back to intervals.
--
-- Uses transcode_sequence() contract, NOT g2p_batch().
-- Count is STRICTLY preserved: output length == input length.

local profile = {}

profile.id           = "ja_transcoder"
profile.display_name = "Japanese Transcoder (Normalize to Stable IPA)"
profile.encoding     = "IPA"

-- ① PASSTHROUGH first — referenced by ensure_dict() at module load time
local PASSTHROUGH = {
    ["_"]=true, ["sil"]=true, ["sp"]=true, ["pau"]=true, ["br"]=true
}

-- ② IPA vowels constant
local IPA_VOWELS = {
    ["a"]=true, ["i"]=true, ["ɯ"]=true, ["e"]=true, ["o"]=true, ["ɴ"]=true
}

-- ③ Lazy-loaded dictionaries
local _surface_dict = nil
local _output_symbols = nil

-- ④ ensure_dict (now safe to reference PASSTHROUGH)
local function ensure_dict()
    if _surface_dict ~= nil then return end
    _surface_dict = vocal.io.load_dictionary("lua/data/surface_to_stable.txt", "=")

    -- Build output symbol set from values (stable IPA symbols this transcoder emits)
    _output_symbols = {}
    local status, content = pcall(vocal.io.read_text, "lua/data/surface_to_stable.txt")
    if status and content then
        for line in content:gmatch("[^\r\n]+") do
            local first = line:sub(1, 1)
            if first ~= "#" and first ~= "" then
                local eq = line:find("=", 1, true)
                if eq then
                    local ipa = line:sub(eq + 1)
                    if ipa and ipa ~= "" then
                        _output_symbols[ipa] = true
                    end
                end
            end
        end
    end
    -- Also add passthrough symbols (they pass through unchanged = valid output)
    for sym, _ in pairs(PASSTHROUGH) do
        _output_symbols[sym] = true
    end
end

--- Pass 1: Normalize pass (1-to-1 symbol mapping).
--- Port of EnhancedJapaneseTranscoder._normalize_pass()
--- encoding: "romaji" or "ipa" — determines how ambiguous 'j' is handled.
local function normalize_pass(phonemes, encoding)
    ensure_dict()
    local normalized = {}

    for _, p in ipairs(phonemes) do
        -- Guard: if token contains a space it is a phrase, not a phoneme — pass through with warning
        if type(p) == "string" and p:find(" ", 1, true) then
            vocal.log("ja_transcoder: received phrase input '" .. p .. "' — use ja_transcriber for text. Passing through.")
            table.insert(normalized, p)
        -- Always pass through silence/boundary markers unchanged
        elseif PASSTHROUGH[p] then
            table.insert(normalized, p)

        -- Special case: 'j' is ambiguous
        -- In Romaji encoding: 'j' → 'dʑ' (the consonant)
        -- In IPA encoding:    'j' → 'j'  (the semivowel)
        elseif p == "j" then
            if encoding == "romaji" then
                table.insert(normalized, "dʑ")
            else
                table.insert(normalized, "j")
            end

        else
            local stable = _surface_dict.get(p)
            if stable ~= nil then
                table.insert(normalized, stable)
            else
                -- Unknown: pass through with warning
                vocal.log("ja_transcoder: unknown symbol in normalize pass: '" .. p .. "'")
                table.insert(normalized, p)
            end
        end
    end

    return normalized
end

--- Pass 2: Enhancement pass (context-aware IPA-to-IPA correction).
--- Port of EnhancedJapaneseTranscoder._enhancement_pass()
--- Operates on already-normalized IPA sequence.
--- COUNT MUST BE PRESERVED.
local function enhancement_pass(phonemes)
    if #phonemes == 0 then return phonemes end

    local corrected = {}
    for _, p in ipairs(phonemes) do
        table.insert(corrected, p)
    end

    -- Handle '-' at start of sequence
    if corrected[1] == "-" then
        corrected[1] = "sil"
    end

    for i = 1, #corrected - 1 do
        local cur  = corrected[i]
        local next = corrected[i + 1]

        -- Skip rules if either token is a passthrough/silence marker
        if PASSTHROUGH[cur] or PASSTHROUGH[next] then
            -- no rules applied across silence boundaries
        else
            -- Rule: '-' after vowel → repeat vowel; after non-vowel → sil
            if next == "-" then
                if IPA_VOWELS[cur] then
                    corrected[i + 1] = cur
                else
                    corrected[i + 1] = "sil"
                end
            end

            -- Rule: 'h' before 'i' → palatalized 'ç'
            if cur == "h" and next == "i" then
                corrected[i] = "ç"
            end

            -- Rule: 't' before 'ɯ' → 'ts'
            -- Rule: 't' before 'i' → 'tɕ'
            if cur == "t" then
                if next == "ɯ" then
                    corrected[i] = "ts"
                elseif next == "i" then
                    corrected[i] = "tɕ"
                end
            end
        end
    end

    return corrected
end

--- Main transcode function.
--- Input:  flat table of single phoneme symbols {"k","o","ch","i","w","a"}
--- Output: flat table of stable IPA symbols    {"k","o","tɕ","i","w","a"}
--- encoding: optional, "romaji" (default) or "ipa"
local function transcode(phonemes, encoding)
    encoding = encoding or "romaji"
    local normalized = normalize_pass(phonemes, encoding)
    local enhanced   = enhancement_pass(normalized)

    -- Strict count check (mirrors Python's fatal error)
    if #enhanced ~= #phonemes then
        vocal.log(
            "ja_transcoder: FATAL count mismatch after enhancement! " ..
            "input=" .. #phonemes .. " output=" .. #enhanced ..
            ". Returning normalized (pre-enhancement) result."
        )
        return normalized
    end

    return enhanced
end

--- Required transcode contract (called by LuaPhonemizerAdapter.TranscodeSequenceAsync)
--- Input:  Lua table of single phoneme strings
--- Output: Lua table of normalized IPA strings (same count)
function profile.transcode_sequence(phonemes)
    -- Default to romaji encoding (most common use case from Python server)
    return transcode(phonemes, "romaji")
end

--- Variant: explicit IPA encoding (for normalizing already-IPA intervals)
function profile.transcode_sequence_ipa(phonemes)
    return transcode(phonemes, "ipa")
end

--- g2p_batch is not the primary contract for this profile,
--- but we implement it as a convenience for single-string input
--- (treats the whole string as space-separated phonemes to normalize)
function profile.g2p_batch(lines)
    ensure_dict()
    local results = {}
    for i = 1, #lines do
        local text = lines[i]
        if type(text) == "string" then
            text = text:match("^%s*(.-)%s*$")
            if text == "" then
                results[i] = ""
            else
                -- Split space-separated phonemes, transcode, rejoin
                local phonemes = {}
                for p in text:gmatch("%S+") do
                    table.insert(phonemes, p)
                end
                local transcoded = transcode(phonemes, "romaji")
                results[i] = table.concat(transcoded, " ")
            end
        else
            results[i] = ""
        end
    end
    return results
end

--- Optional: validate_symbol
function profile.validate_symbol(symbol)
    ensure_dict()
    return (_output_symbols ~= nil and _output_symbols[symbol] == true)
end

-- Eagerly load dict so supported_symbols is available immediately for compatibility scoring
ensure_dict()

-- Expose output symbols as array for C# SupportedSymbols property
profile.supported_symbols = {}
if _output_symbols then
    local i = 1
    for sym, _ in pairs(_output_symbols) do
        profile.supported_symbols[i] = sym
        i = i + 1
    end
end

return profile
