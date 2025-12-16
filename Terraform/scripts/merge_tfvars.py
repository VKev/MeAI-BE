import json
import glob
import os


def deep_merge(into: dict, src: dict) -> None:
    for key, value in src.items():
        if key in into and isinstance(into[key], dict) and isinstance(value, dict):
            deep_merge(into[key], value)
        else:
            into[key] = value


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
        return hcl2.load(f)


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


