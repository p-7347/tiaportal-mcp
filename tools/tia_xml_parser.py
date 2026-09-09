#!/usr/bin/env python3
"""
TIA Portal XML Block Parser
TIA Portal export XML을 압축된 읽기 쉬운 텍스트로 변환
"""

import xml.etree.ElementTree as ET
import sys
import io
from collections import Counter

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='replace')

NS_IF     = 'http://www.siemens.com/automation/Openness/SW/Interface/v5'
NS_FLGNET = 'http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v5'
NS_SCL    = 'http://www.siemens.com/automation/Openness/SW/NetworkSource/Scl/v5'
NS_ST     = 'http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v4'


def symbol_name(access_elem):
    comps = access_elem.findall(f'.//{{{NS_FLGNET}}}Component')
    if comps:
        return '.'.join(c.get('Name', '') for c in comps)
    # namespace 없이 시도
    comps = access_elem.findall('.//Component')
    return '.'.join(c.get('Name', '') for c in comps) if comps else '?'


def get_mlt_text(parent, composition_name):
    """MultilingualText에서 텍스트 추출"""
    for mlt in parent.findall(f'ObjectList/MultilingualText'):
        if mlt.get('CompositionName') == composition_name:
            for item in mlt.findall('.//MultilingualTextItem'):
                t = item.findtext('AttributeList/Text', '').strip()
                if t:
                    return t
    return ''


def parse_interface(attr_elem):
    lines = []
    sections_elem = attr_elem.find(f'Interface/{{{NS_IF}}}Sections')
    if sections_elem is None:
        return lines
    for section in sections_elem.findall(f'{{{NS_IF}}}Section'):
        sec_name = section.get('Name', '')
        members = section.findall(f'{{{NS_IF}}}Member')
        if not members:
            continue
        lines.append(f'  [{sec_name}]')
        for m in members:
            dtype = m.get('Datatype', '')
            lines.append(f'    {m.get("Name")}: {dtype}')
            for sub in m.findall(f'.//{{{NS_IF}}}Member'):
                lines.append(f'      .{sub.get("Name")}: {sub.get("Datatype","")}')
    return lines


def parse_network_lad(flgnet_elem):
    """LAD 네트워크에서 변수/파트 추출"""
    lines = []

    # 변수 참조 수집 (중복 제거, 순서 유지)
    vars_seen = {}
    for acc in flgnet_elem.findall(f'{{{NS_FLGNET}}}Parts/{{{NS_FLGNET}}}Access'):
        scope = acc.get('Scope', '')
        name = symbol_name(acc)
        if name and name != '?':
            vars_seen[name] = scope

    # Parts 수집
    contacts_normal, contacts_neg, coils, coils_set, coils_reset, calls, boxes = [], [], [], [], [], [], []
    for part in flgnet_elem.findall(f'{{{NS_FLGNET}}}Parts/{{{NS_FLGNET}}}Part'):
        pname = part.get('Name', '')
        negated = part.find(f'{{{NS_FLGNET}}}Negated') is not None

        if pname == 'Contact':
            (contacts_neg if negated else contacts_normal).append(pname)
        elif pname == 'Coil':
            coils.append(pname)
        elif pname == 'SCoil':
            coils_set.append(pname)
        elif pname == 'RCoil':
            coils_reset.append(pname)
        elif pname == 'Call':
            called = part.find(f'{{{NS_FLGNET}}}TemplateValue[@Name="BlockName"]')
            calls.append(called.text if called is not None else '?')
        else:
            boxes.append(pname)

    # 변수 출력
    if vars_seen:
        # 접점/코일 관련 변수 분리
        var_list = list(vars_seen.keys())
        lines.append(f'    Vars({len(var_list)}): {", ".join(var_list)}')

    # 구조 요약
    summary = []
    if contacts_normal:
        summary.append(f'Contact x{len(contacts_normal)}')
    if contacts_neg:
        summary.append(f'Contact(N) x{len(contacts_neg)}')
    if coils:
        summary.append(f'Coil x{len(coils)}')
    if coils_set:
        summary.append(f'SCoil x{len(coils_set)}')
    if coils_reset:
        summary.append(f'RCoil x{len(coils_reset)}')
    if calls:
        summary.append(f'CALL: {", ".join(calls)}')
    if boxes:
        cnt = Counter(boxes)
        summary.append(', '.join(f'{k} x{v}' if v > 1 else k for k, v in cnt.items()))
    if summary:
        lines.append(f'    Parts: {" | ".join(summary)}')

    return lines


def parse_network_scl(scl_elem):
    """SCL 네트워크 소스 추출 (압축)"""
    lines = []
    text = scl_elem.text or ''
    src_lines = [l for l in text.split('\n') if l.strip() and not l.strip().startswith('//')]
    limit = 40
    for l in src_lines[:limit]:
        lines.append(f'    {l.rstrip()}')
    if len(src_lines) > limit:
        lines.append(f'    ... ({len(src_lines) - limit} lines omitted)')
    return lines


def _find_all(elem, local_name):
    """네임스페이스 무관하게 로컬명으로 자식 검색"""
    return [c for c in elem.findall('.//{*}' + local_name)]


def _find(elem, local_name):
    return elem.find('{*}' + local_name)


def _components(elem):
    comps = _find_all(elem, 'Component')
    return '.'.join(c.get('Name', '') for c in comps)


def reconstruct_st(elem):
    """StructuredText v4 토큰 기반 SCL 재구성"""
    parts = []
    for child in elem:
        tag = child.tag.split('}')[-1]
        if tag == 'Token':
            parts.append(child.get('Text', ''))
        elif tag == 'Blank':
            num = int(child.get('Num', 1))
            parts.append(' ' * num)
        elif tag == 'NewLine':
            num = int(child.get('Num', 1))
            parts.append('\n' * num)
        elif tag == 'Access':
            scope = child.get('Scope', '')
            if scope == 'Call':
                ci = _find(child, 'CallInfo')
                if ci is not None:
                    inst = _find(ci, 'Instance')
                    inst_name = _components(inst) if inst is not None else ci.get('Name', '?')
                    params = []
                    for param in ci:
                        ptag = param.tag.split('}')[-1]
                        if ptag in ('Parameter', 'NamelessParameter'):
                            params.append(reconstruct_st(param))
                    parts.append(f'{inst_name}({", ".join(params)})')
            elif scope in ('LocalVariable', 'GlobalVariable'):
                parts.append(_components(child))
            elif scope == 'LiteralConstant':
                val = child.findtext('.//{*}ConstantValue', '')
                parts.append(val)
            elif scope == 'TypedConstant':
                val = child.findtext('.//{*}ConstantValue', '')
                typ = child.findtext('.//{*}ConstantType', '')
                parts.append(f'{typ}#{val}' if typ else val)
            else:
                c = _components(child)
                if c:
                    parts.append(c)
        else:
            parts.append(reconstruct_st(child))
    return ''.join(parts)


def parse_network_structured_text(st_elem):
    """StructuredText v4 네트워크 파싱 후 압축 출력"""
    raw = reconstruct_st(st_elem)
    src_lines = [l for l in raw.split('\n') if l.strip()]
    limit = 40
    result = []
    for l in src_lines[:limit]:
        result.append(f'    {l.rstrip()}')
    if len(src_lines) > limit:
        result.append(f'    ... ({len(src_lines) - limit} lines omitted)')
    return result


def parse_block(xml_path):
    with open(xml_path, encoding='utf-8') as f:
        tree = ET.parse(f)
    root = tree.getroot()

    out = []

    # 블록 타입 찾기
    block_elem = None
    block_type = ''
    for tag in ['SW.Blocks.FB', 'SW.Blocks.FC', 'SW.Blocks.OB', 'SW.Blocks.GlobalDB', 'SW.Blocks.InstanceDB']:
        block_elem = root.find(tag)
        if block_elem is not None:
            block_type = tag.split('.')[-1]
            break

    if block_elem is None:
        return "알 수 없는 블록 형식"

    attr = block_elem.find('AttributeList')
    name = attr.findtext('Name', '?')
    number = attr.findtext('Number', '?')
    lang = attr.findtext('ProgrammingLanguage', '?')

    out.append('=' * 60)
    out.append(f'Block: {name}  ({block_type}{number}, {lang})')
    out.append('=' * 60)

    # 인터페이스
    iface_lines = parse_interface(attr)
    if iface_lines:
        out.append('\n[INTERFACE]')
        out.extend(iface_lines)

    # 네트워크
    compile_units = block_elem.findall('.//SW.Blocks.CompileUnit')
    if compile_units:
        out.append(f'\n[NETWORKS]  ({len(compile_units)} networks)')

    for i, cu in enumerate(compile_units, 1):
        title = get_mlt_text(cu, 'Title')
        comment = get_mlt_text(cu, 'Comment')
        header = f'\n  Network {i}'
        if title:
            header += f': {title}'
        out.append(header)
        if comment:
            out.append(f'    // {comment[:100]}')

        cu_attr = cu.find('AttributeList')
        if cu_attr is None:
            continue

        # SCL (plain text, v5)
        scl_elem = cu_attr.find(f'NetworkSource/{{{NS_SCL}}}StructuredText')
        if scl_elem is not None:
            out.extend(parse_network_scl(scl_elem))
            continue

        # StructuredText token-based (v4) — FOR/IF/CASE 등 SCL
        st_elem = cu_attr.find(f'NetworkSource/{{{NS_ST}}}StructuredText')
        if st_elem is not None:
            out.extend(parse_network_structured_text(st_elem))
            continue

        # LAD/FBD
        flgnet = cu_attr.find(f'NetworkSource/{{{NS_FLGNET}}}FlgNet')
        if flgnet is not None:
            out.extend(parse_network_lad(flgnet))
            continue

        out.append('    (empty network)')

    return '\n'.join(out)


if __name__ == '__main__':
    if len(sys.argv) < 2:
        print('Usage: python tia_xml_parser.py <block.xml> [output.txt]')
        sys.exit(1)

    result = parse_block(sys.argv[1])

    if len(sys.argv) >= 3:
        with open(sys.argv[2], 'w', encoding='utf-8') as f:
            f.write(result)
        print(f'Saved: {sys.argv[2]}')
    else:
        print(result)
