"""The dictionaries that describe each file, in UniVerse's own D-type format.

A UniVerse file has two parts: the data, and a dictionary describing it. The
dictionary is what makes a MultiValue database self-describing — someone handed
an account they have never seen runs `LIST DICT INVENTORY` and learns that field
2 is on-hand quantity, that it is multi-valued, and how to display it.

This store held no dictionaries at all until now, and the gap mattered more than
it looks. The MCP server's discovery tools — `list_dictionary`,
`describe_file`, `get_field_definition` — exist precisely so that someone can
point the server at their own database and find out what is in it. Against this
store they returned nothing, so the one path a stranger would take first was the
one path never exercised end to end.

A dictionary item is an ordinary record. The layout is UniVerse's, not this
project's:

    Field 1  Type -- D for a data descriptor, I for a calculated one
    Field 2  Location -- which field of the data record it describes, 0 for @ID
    Field 3  Conversion code -- how the stored value becomes a displayed one
    Field 4  Heading -- the column title
    Field 5  Format -- width and justification, e.g. 10L or 8R
    Field 6  S or M -- single-valued, or multi-valued

The conversion codes are real: `MD2` is two implied decimal places, `D2/` is a
date shown as MM/DD/YY. They are included rather than left blank because a
dictionary without them describes the shape of the data and not its meaning, and
the meaning is the half a stranger cannot guess.
"""

from __future__ import annotations

# One entry per dictionary item: name -> (type, location, conversion, heading,
# format, single or multi). Written out in full rather than generated, because a
# dictionary is documentation and reading it is the point.
DictionaryItem = tuple[str, str, str, str, str, str]

# Every file carries @ID. UniVerse creates it; it is stated here so that a
# stranger listing a dictionary sees the key described alongside everything else,
# which is what they would see against a real account.
_KEY = ("D", "0", "", "Key", "16L", "S")

DICTIONARIES: dict[str, dict[str, DictionaryItem]] = {
    "PRODUCT": {
        "@ID": _KEY,
        "PART.NUMBER": ("D", "0", "", "Part Number", "16L", "S"),
        "DESCRIPTION": ("D", "1", "", "Description", "30L", "S"),
        "MANUFACTURER": ("D", "2", "", "Manufacturer", "20L", "S"),
        "MFR.PART": ("D", "3", "", "Mfr Part", "18L", "S"),
        "UOM": ("D", "4", "", "UOM", "4L", "S"),
        "CATEGORY": ("D", "5", "", "Category", "6L", "S"),
        # Money is stored as an integer number of cents and displayed with two
        # decimal places, which is what MD2 means. A reader who missed this would
        # report every price a hundred times too large.
        "LIST.PRICE": ("D", "6", "MD2", "List Price", "10R", "S"),
        "STATUS": ("D", "7", "", "Status", "2L", "S"),
    },
    "INVENTORY": {
        "@ID": _KEY,
        # These five are parallel: position n of each belongs to the same branch.
        # The dictionary says so by marking every one of them multi-valued, which
        # is the only warning a stranger gets that reading position 3 of one
        # beside position 4 of another produces a plausible, wrong answer.
        "BRANCH": ("D", "1", "", "Branch", "6L", "M"),
        "ON.HAND": ("D", "2", "", "On Hand", "8R", "M"),
        "COMMITTED": ("D", "3", "", "Committed", "10R", "M"),
        "ON.ORDER": ("D", "4", "", "On Order", "9R", "M"),
        "BIN": ("D", "5", "", "Bin", "8L", "M"),
    },
    "BRANCH": {
        "@ID": _KEY,
        "BRANCH.NAME": ("D", "1", "", "Branch Name", "24L", "S"),
        "CITY": ("D", "2", "", "City", "18L", "S"),
        "REGION": ("D", "3", "", "Region", "10L", "S"),
        "TELEPHONE": ("D", "4", "", "Telephone", "14L", "S"),
    },
    "CUSTOMER": {
        "@ID": _KEY,
        "CUST.NAME": ("D", "1", "", "Customer", "28L", "S"),
        "ADDRESS": ("D", "2", "", "Address", "30L", "M"),
        "CONTACT": ("D", "3", "", "Contact", "22L", "M"),
        # Multi-valued by contact and sub-valued by number, so one contact may
        # have several. The dictionary can only say "multi-valued"; the
        # sub-valuing is visible in the data.
        "PHONE": ("D", "4", "", "Phone", "14L", "M"),
        "TERMS": ("D", "5", "", "Terms", "8L", "S"),
        "PRICE.CLASS": ("D", "6", "", "Price Class", "10L", "S"),
        "HOME.BRANCH": ("D", "7", "", "Home Branch", "6L", "S"),
    },
    "PRICING": {
        "@ID": _KEY,
        # Stored as ten-thousandths so that a multiplier of 0.8250 is exact.
        "MULTIPLIER": ("D", "1", "MD4", "Multiplier", "10R", "M"),
        "FROM.DATE": ("D", "2", "D2/", "From", "10R", "M"),
        "TO.DATE": ("D", "3", "D2/", "To", "10R", "M"),
    },
    "ORDER": {
        "@ID": _KEY,
        "CUSTOMER": ("D", "1", "", "Customer", "12L", "S"),
        "ORDER.DATE": ("D", "2", "D2/", "Ordered", "10R", "S"),
        "STATE": ("D", "3", "", "State", "12L", "S"),
        # Parallel again, one position per line on the order.
        "LINE.PART": ("D", "4", "", "Part", "16L", "M"),
        "LINE.QTY": ("D", "5", "", "Qty", "8R", "M"),
        "LINE.BRANCH": ("D", "6", "", "Branch", "6L", "M"),
        "LINE.PROMISED": ("D", "7", "D2/", "Promised", "10R", "M"),
    },
}

# How a dictionary file is named in the store. UniVerse writes `DICT PRODUCT`
# with a space; a file on disk cannot, so the store holds `DICT.PRODUCT` and the
# driver translates. The translation lives in one place for the same reason every
# other layout does.
DICTIONARY_PREFIX = "DICT."


def store_name_for(file_name: str) -> str:
    """Return the store file holding one file's dictionary.

    Args:
        file_name: The data file's name, with or without a `DICT ` prefix

    Returns:
        The name of the store file holding that dictionary
    """
    bare = file_name.upper()
    if bare.startswith("DICT "):
        bare = bare[len("DICT "):].strip()
    elif bare.startswith(DICTIONARY_PREFIX):
        bare = bare[len(DICTIONARY_PREFIX):].strip()

    return f"{DICTIONARY_PREFIX}{bare}"


def is_dictionary_name(file_name: str) -> bool:
    """Whether a name refers to a dictionary rather than to data.

    Args:
        file_name: The name as the caller wrote it

    Returns:
        True for `DICT PRODUCT` and `DICT.PRODUCT`, False for `PRODUCT`
    """
    upper = file_name.upper()
    return upper.startswith("DICT ") or upper.startswith(DICTIONARY_PREFIX)


def fields_for(file_name: str) -> dict[str, DictionaryItem]:
    """Return the dictionary items for one file.

    Args:
        file_name: The data file's name

    Returns:
        Item name to definition, empty when the file has no dictionary here
    """
    return DICTIONARIES.get(file_name.upper(), {})
