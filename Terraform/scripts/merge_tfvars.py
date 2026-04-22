import json
import glob
import os
import re


_HEREDOC_PATTERN = re.compile(
    r'^<<-?([A-Za-z_][A-Za-z0-9_]*)\n(.*)\n\1$',
    re.DOTALL,
)


def deep_merge(into: dict, src: dict) -> None:
    for key, value in src.items():
        if str(key).startswith("__"):
            continue
        if key in into and isinstance(into[key], dict) and isinstance(value, dict):
            deep_merge(into[key], value)
        else:
            into[key] = value


def _strip_hcl_quotes(value):
    """Clean up string values that `python-hcl2` mangles on parse.

    Two distinct bugs to undo:

    1. Literal surrounding double quotes on every string: `region = "us-east-1"`
       parses to the Python string `'"us-east-1"'` (with the `"` chars inside the
       value). If passed through, $GITHUB_ENV emits `AWS_REGION="us-east-1"` and
       aws-actions/configure-aws-credentials rejects it as an invalid region.

    2. Heredoc delimiters left in the string body: `manifest = <<-EOT\n...\nEOT`
       parses to `'"<<-EOT\\n...\\nEOT"'`. Terraform's kubectl_file_documents then
       tries to YAML-parse the value, hits `<<-EOT` on line 1, fails at line 2 with
       "mapping values are not allowed in this context".

    Stripping both is safe: HCL string literals never legitimately start AND end
    with `"` (it's the delimiter), and heredocs are only ever structural syntax.
    Recurses into lists/dicts so nested values are cleaned too.
    """
    if isinstance(value, str):
        # Outer quote pair → strip first.
        if len(value) >= 2 and value[0] == '"' and value[-1] == '"':
            value = value[1:-1]
        # Heredoc markers → strip the `<<-TAG\n` header and `\nTAG` footer; keep
        # the body. `<<-` and `<<` both match; the closing tag is captured by
        # backreference so mismatched EOT/END/HEREDOC can't accidentally trigger.
        heredoc = _HEREDOC_PATTERN.match(value)
        if heredoc:
            value = heredoc.group(2)
        return value
    if isinstance(value, list):
        return [_strip_hcl_quotes(v) for v in value]
    if isinstance(value, dict):
        return {k: _strip_hcl_quotes(v) for k, v in value.items()}
    return value


def load_file(path: str):
    if path.endswith('.json'):
        with open(path, 'r', encoding='utf-8') as f:
            return json.load(f)
    # HCL
    try:
        import hcl2
    except Exception as exc:
        raise RuntimeError(f"python-hcl2 not available to parse {path}: {exc}")
    with open(path, 'r', encoding='utf-8') as f:
        parsed = hcl2.load(f)
    # Run the quote-strip pass only on HCL-parsed data. JSON tfvars already give
    # us clean strings via json.load(), so no need to touch them.
    return _strip_hcl_quotes(parsed)


def main():
    # Run inside Terraform working directory
    paths = sorted(glob.glob('*.auto.tfvars')) + sorted(glob.glob('*.auto.tfvars.json'))
    merged = {}
    for p in paths:
        try:
            data = load_file(p)
            if isinstance(data, dict):
                deep_merge(merged, data)
            else:
                print(f"Skip {p}: not a dict root")
        except Exception as e:
            print(f"Skip {p}: {e}")

    with open('00-all.auto.tfvars.json', 'w', encoding='utf-8') as f:
        json.dump(merged, f)

    print('Merged files:', paths)
    print('Final keys:', list(merged.keys()))


if __name__ == '__main__':
    main()


