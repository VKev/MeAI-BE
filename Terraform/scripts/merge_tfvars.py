import json
import glob
import os


def deep_merge(into: dict, src: dict) -> None:
    for key, value in src.items():
        if key in into and isinstance(into[key], dict) and isinstance(value, dict):
            deep_merge(into[key], value)
        else:
            into[key] = value


def _strip_hcl_quotes(value):
    """Strip literal surrounding double quotes that `python-hcl2` embeds in parsed
    string values. `region = "us-east-1"` comes back from hcl2.load() as the Python
    string `'"us-east-1"'` (with quote chars inside the value) on current library
    versions; if we pass that through, downstream consumers (export_tf_env.py →
    $GITHUB_ENV → aws-actions/configure-aws-credentials) see `"us-east-1"` with
    quotes included and reject it as an invalid region.

    HCL string values never legitimately start AND end with literal `"`, since `"`
    is the delimiter in HCL syntax — so stripping one matched outer pair here is
    safe. Recurses into lists/dicts so nested values (e.g. rds.user.password) are
    cleaned too.
    """
    if isinstance(value, str):
        if len(value) >= 2 and value[0] == '"' and value[-1] == '"':
            return value[1:-1]
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


