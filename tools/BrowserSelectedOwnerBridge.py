"""Asynchronous ingress inside one selected app owner's held source span.

One admission/renewal worker owns effects. Status and stop ingress never launch
another renewer, kill a process, or infer retirement from callback return.
"""
import hashlib,hmac,inspect,json,os,pathlib,threading,uuid

class SelectedOwnerBridge:
    def __init__(self, *, owner, admission_factory, whole_close, current_session,
                 current_actors, lane_available, journal, binding, token):
        self.owner,self.admission_factory,self.whole_close=owner,admission_factory,whole_close
        self.current_session,self.current_actors,self.lane_available=current_session,current_actors,lane_available
        self.journal=pathlib.Path(journal).resolve(strict=True)
        if set(binding)!={'generation','session','actors','sourcePins'} or set(binding['actors'])!={'Q','W'} or len(token)<32 or not self.journal.is_dir():
            raise ValueError('Closed selected owner/actor/source binding required')
        self.binding,self.token=json.loads(json.dumps(binding)),token
        self.generation,self.session=binding['generation'],binding['session'];self.selection_ticket=self.session;self.session_anchored=False
        # Actual pinned owner/factory/close implementation, not a browser callback.
        for value in (type(owner),admission_factory,whole_close,current_session,current_actors):
            path=pathlib.Path(inspect.getsourcefile(value)).resolve(strict=True)
            if not any(path==pathlib.Path(pin['path']).resolve(strict=True) for pin in binding['sourcePins']):
                raise ValueError('Owner implementation is outside selected source closure')
        if not any(pathlib.Path(__file__).resolve()==pathlib.Path(pin['path']).resolve(strict=True) for pin in binding['sourcePins']):
            raise ValueError('Bridge implementation is outside selected source closure')
        for actor in binding['actors'].values():
            if set(actor)!={'processEpoch','nativeGeneration'} or not isinstance(actor['processEpoch'],str) or not isinstance(actor['nativeGeneration'],str) or not actor['nativeGeneration'].isdigit():
                raise ValueError('Opaque app/native actor identities required')
        self.native_lock=owner.native_lock
        self.lock=threading.RLock();self.thread=None;self.active_id=None;self.close_id=None
        self.cancel=threading.Event();self.admission_ended=threading.Event()
        self._sources()

    def _sources(self):
        for pin in self.binding['sourcePins']:
            path=pathlib.Path(pin['path'])
            if not path.is_absolute() or hashlib.sha256(path.read_bytes()).hexdigest()!=pin['sha256']:
                raise ValueError('Selected owner/factory source changed')

    def _effect_guard(self):
        self._sources();self.owner.assert_span_live()
        if self.current_actors()!=self.binding['actors'] or self.current_session()!=self.session:
            raise ValueError('Live owner actor/session changed')

    def _authenticate(self,bearer):
        if not hmac.compare_digest(bearer,self.token):raise PermissionError('Owner authentication denied')

    def _response(self,state,request_id,receipt,completed=False):
        return dict(state=state,requestId=request_id,generation=self.generation,session=self.selection_ticket,actualNativeSession=self.session if self.session_anchored else None,receipt=receipt,actualEffectsCompleted=completed)

    def _write(self,request_id,response):
        target=self.journal/(request_id+'.result.json');temporary=self.journal/(request_id+'.'+uuid.uuid4().hex+'.tmp')
        with temporary.open('x',encoding='utf-8') as stream:
            json.dump(response,stream);stream.flush();os.fsync(stream.fileno())
        os.replace(temporary,target)
        return response

    def status(self,request_id,bearer):
        self._authenticate(bearer)
        if str(uuid.UUID(request_id))!=request_id:raise ValueError('Canonical request ID required')
        with self.lock:
            if not (self.journal/(request_id+'.intent.json')).exists():raise ValueError('Unknown request')
            result=self.journal/(request_id+'.result.json')
            response=json.loads(result.read_text()) if result.exists() else self._response('unknown',request_id,'Durable intent exists; outcome unproven')
            if request_id not in (self.active_id,self.close_id) and not response['actualEffectsCompleted']:
                return self._response('unknown',request_id,'Retained prior-owner intent/result; current worker ownership unproven')
            return response

    def dispatch(self,request,bearer):
        self._authenticate(bearer)
        if set(request)!={'schema','action','direction','generation','requestId','session'} or request['schema']!='questms.owner.action.v1' or request['action'] not in ('establish','disconnect') or request['direction']!='both' or request['generation']!=self.generation or request['session']!=self.selection_ticket:
            raise ValueError('Closed selected bilateral owner request required')
        request_id=str(uuid.UUID(request['requestId']))
        if request_id!=request['requestId']:raise ValueError('Canonical request ID required')
        canonical=json.dumps(request,sort_keys=True,separators=(',',':')).encode()
        intent=self.journal/(request_id+'.intent.json')
        with self.lock:
            if intent.exists():
                if intent.read_bytes()!=canonical:raise ValueError('Conflicting owner request replay')
                return self.status(request_id,bearer)
            if request['action']=='establish':
                if self.active_id is not None or not self.lane_available():raise RuntimeError('Admission/maintenance already owns lane')
                # Inert closed guards before dispatch; rechecked again at actual effect.
                self._sources()
            else:
                if self.active_id is None or self.close_id is not None:raise RuntimeError('No selected active worker or close already requested')
            with intent.open('xb') as stream:stream.write(canonical);stream.flush();os.fsync(stream.fileno())
            response=self._write(request_id,self._response('queued',request_id,'Durable owner request; no completion claim'))
            if request['action']=='establish':
                self.active_id=request_id
                self.thread=threading.Thread(target=self._run_admission,name='selected-owner-admission',daemon=False)
                self.thread.start()
            else:
                self.close_id=request_id
                # This is a cancellation REQUEST, not a native effect or cleanup.
                # Existing worker cancellation prevents subsequent dispatch; it never
                # cancels/kills a native call currently waiting for actual EOF.
                self.cancel.set()
                if self.admission_ended.is_set():
                    self.thread=threading.Thread(target=self._close,name='selected-owner-close',daemon=False)
                    self.thread.start()
            return response

    def _run_admission(self):
        request_id=self.active_id
        try:
            with self.native_lock:self._effect_guard()
            with self.lock:self._write(request_id,self._response('running',request_id,'Sole guarded admission/renewal worker running'))
            # Factory must pass this callback into existing AdmissionMaintenance.
            # Existing owner.call serializes every native dispatch on native_lock.
            worker=self.admission_factory(cancelled=self.cancel.is_set)
            outcome=worker.run()
            with self.lock:
                state='effective' if outcome.admission_published and outcome.error is None else 'unknown'
                self._write(request_id,self._response(state,request_id,'Owner '+outcome.status+'; effects/cleanup require separate current receipts'))
        except BaseException:
            with self.lock:self._write(request_id,self._response('unknown',request_id,'Original worker outcome unresolved; replay denied'))
        finally:
            with self.lock:
                self.admission_ended.set()
                close=self.close_id is not None
            if close:self._close()  # SAME worker, after actual owned run return.

    def anchor_validated_session(self,request_id,native_session):
        """Only the selected sole worker calls after bilateral signed pair/arm validation.

        Immutable ingress selection ticket stays separate from the actual native
        session created by pair_session. No HTTP route accepts a transition.
        """
        with self.native_lock:
            self._sources();self.owner.assert_span_live()
            if not isinstance(native_session,str) or not native_session or len(native_session)>256 or self.current_session()!=native_session or self.current_actors()!=self.binding['actors']:raise ValueError('Validated current session/actors differ')
            with self.lock:
                if request_id!=self.active_id or self.admission_ended.is_set() or self.session_anchored:raise ValueError('No sole active once-only session transition')
                value=dict(schema='questms.owner.validated_session_transition.v1',requestId=request_id,generation=self.generation,selectionTicket=self.selection_ticket,actualNativeSession=native_session,actors=self.binding['actors'])
                with (self.journal/(request_id+'.session-transition.json')).open('x',encoding='utf-8') as stream:json.dump(value,stream);stream.flush();os.fsync(stream.fileno())
                self.session=native_session;self.session_anchored=True
            self._effect_guard()

    def publish_effective(self,request_id,receipt):
        """Call only from existing owner after its admission receipt validator.

        Projects owner progress, never sets actual completion/cleanup. No HTTP
        route accepts this callback or a caller-supplied success flag.
        """
        with self.native_lock:
            self._effect_guard()
            with self.lock:
                if request_id!=self.active_id or self.admission_ended.is_set() or not receipt:
                    raise ValueError('Effective readback does not bind active worker')
                self._write(request_id,self._response('effective',request_id,receipt))

    def _close(self):
        request_id=self.close_id
        try:
            with self.native_lock:
                self._effect_guard()
                with self.lock:self._write(request_id,self._response('running',request_id,'Serialized owner close; actual barriers pending'))
                closed=self.whole_close(request_id)
                self._effect_guard()
            with self.lock:
                completed=closed['actual_terminal_cleanup'] is True
                self._write(request_id,self._response('effective' if completed else 'unknown',request_id,closed['receipt'],completed))
                # No successor/restart in this bridge even after genuine cleanup.
        except BaseException:
            with self.lock:self._write(request_id,self._response('unknown',request_id,'Selected close outcome unresolved; no kill/replay'))

    def join(self,timeout=None):
        if self.thread is not None:self.thread.join(timeout)
        return self.thread is None or not self.thread.is_alive()


def serve_on_owner_lane(bridge,port,stop_requested=None,on_listening=None):
    """Ingress threads do I/O only; the bridge owns exactly one effect worker."""
    from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*_):pass
        def respond(self,operation):
            code=200
            try:result=operation()
            except PermissionError:code=401;result=dict(error='Authentication denied')
            except ValueError:code=400;result=dict(error='Invalid owner request')
            except Exception:code=409;result=dict(error='Owner unavailable; retain request ID')
            response=json.dumps(result).encode();self.send_response(code);self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(response)));self.end_headers()
            try:self.wfile.write(response)
            except (BrokenPipeError,ConnectionResetError):pass
        def bearer(self):return self.headers.get('Authorization','').removeprefix('Bearer ')
        def do_GET(self):
            def read():
                if not self.path.startswith('/operator/'):raise ValueError('Closed status endpoint required')
                return bridge.status(self.path.removeprefix('/operator/'),self.bearer())
            self.respond(read)
        def do_POST(self):
            def dispatch():
                if self.path!='/operator':raise ValueError('Closed owner endpoint required')
                length=int(self.headers.get('Content-Length','0'))
                if not 0<length<=4096:raise ValueError('Bounded owner request required')
                self.connection.settimeout(10);raw=self.rfile.read(length)
                if len(raw)!=length:raise ValueError('Truncated owner request')
                return bridge.dispatch(json.loads(raw),self.bearer())
            self.respond(dispatch)
    if type(port) is not int or not 1024<=port<=65535:raise ValueError('Explicit leased port required')
    with ThreadingHTTPServer(('127.0.0.1',port),Handler) as server:
        if on_listening is not None:on_listening()
        if stop_requested is None:server.serve_forever()
        else:
            server.timeout=.5
            while not stop_requested():server.handle_request()
