"""Read-only projection of explicitly selected, completed native owner receipts.

No ADB, renewal, directory scanning, signatures, acceptance or lifecycle effects.
Input config and output belong in ignored private evidence directories.
"""
import argparse, datetime, hashlib, json, pathlib

def project(config):
    if set(config) != {'owner', 'generation', 'session', 'challenge', 'apkSha256', 'receipts'} or set(config['receipts']) != {'Q', 'W'}:
        raise ValueError('Closed owner receipt selection required')
    observations = {}
    for peer, filename in config['receipts'].items():
        path = pathlib.Path(filename)
        if not path.is_absolute(): raise ValueError('Absolute selected receipt required')
        raw = path.read_bytes()
        if len(raw) > 1048576: raise ValueError('Receipt exceeds 1 MiB')
        receipt = json.loads(raw)
        if receipt['schema'] != 'rusty.quest.stereo.concurrent_qualification_receipt.v1' or receipt['action'] != 'peer_status' or receipt['challenge'] != config['challenge'] or receipt['apk_sha256'] != config['apkSha256']:
            raise ValueError('Owner receipt identity differs')
        life = receipt['peer_lifecycle']; authority = life['authority_projection']
        if authority['peer_session_id'] != config['session']: raise ValueError('Owner session differs')
        observed, expires = life['observed_at_ms'], authority['expires_at_ms']
        if type(observed) is not int or type(expires) is not int or expires <= observed:
            raise ValueError('Owner observation/expiry invalid')
        utc = lambda ms: datetime.datetime.fromtimestamp(ms / 1000, datetime.timezone.utc).isoformat().replace('+00:00','Z')
        source = path.name + ' sha256:' + hashlib.sha256(raw).hexdigest()
        # Completed peer_status establishes control readback; no new device-current claim.
        observations[peer + '.control'] = dict(state='connected', observedUtc=utc(observed), expiresUtc=utc(min(expires, observed+5000)), receipt=source, session=config['session'])
        direction = 'Q→W' if peer == 'W' else 'W→Q'
        snapshot = receipt['native_snapshot']
        if len(snapshot) != 160 or snapshot[:2] != [1,160]: raise ValueError('Native snapshot ABI differs')
        # Adoption count is displayed as evidence, not proof of an open socket/handshake.
        observations[direction + '.peer'] = dict(state='active' if life['status']=='active' else 'failed', observedUtc=utc(observed), expiresUtc=utc(min(expires,observed+5000)), receipt=source+' · recorded peer adoptions '+str(snapshot[56]), session=config['session'], frames=snapshot[56], handshake=False, socketOpen=False)
    return dict(schema='questms.hub.observations.v1',owner=config['owner']+' · raw receipt projection, signatures/qualification unverified',generation=config['generation'],observations=observations)

if __name__ == '__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--selection',required=True);parser.add_argument('--output',required=True);args=parser.parse_args()
    output=pathlib.Path(args.output)
    if not output.is_absolute(): raise ValueError('Absolute output required')
    value=project(json.loads(pathlib.Path(args.selection).read_text(encoding='utf-8-sig')))
    with output.open('x',encoding='utf-8') as stream: json.dump(value,stream,ensure_ascii=False,indent=2)
