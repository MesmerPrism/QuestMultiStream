import importlib.util,pathlib,tempfile,types,unittest,uuid,threading,time,hashlib
spec=importlib.util.spec_from_file_location('bridge',pathlib.Path(__file__).with_name('BrowserSelectedOwnerBridge.py'));module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
ACTORS={'Q':{'processEpoch':'app-process.fixture.q','nativeGeneration':'9223372036854775800'},'W':{'processEpoch':'app-process.fixture.w','nativeGeneration':'9223372036854775799'}}
class ModeledOwner:
    def __init__(self):self.native_lock=threading.RLock();self.guards=0
    def assert_span_live(self):self.guards+=1
class BridgeTests(unittest.TestCase):
    def make(self,folder,factory,close,available=lambda:True):
        current_session=lambda:'session.fixture';current_actors=lambda:ACTORS
        pin=lambda path:dict(path=str(path),sha256=hashlib.sha256(path.read_bytes()).hexdigest())
        binding=dict(generation='fixture',session='session.fixture',actors=ACTORS,sourcePins=[pin(pathlib.Path(__file__).resolve()),pin(pathlib.Path(module.__file__).resolve())])
        return module.SelectedOwnerBridge(owner=ModeledOwner(),admission_factory=factory,whole_close=close,current_session=current_session,current_actors=current_actors,lane_available=available,journal=folder,binding=binding,token='t'*32)
    def request(self,action='establish'):
        return dict(schema='questms.owner.action.v1',action=action,direction='both',generation='fixture',session='session.fixture',requestId=str(uuid.uuid4()))
    def test_blocked_start_status_and_stop_once(self):
        entered=threading.Event();release=threading.Event();calls=[]
        with tempfile.TemporaryDirectory() as folder:
            def factory(*,cancelled):
                def run():
                    calls.append('start');entered.set()
                    with bridge.native_lock:release.wait(5)
                    self.assertTrue(cancelled())
                    return types.SimpleNamespace(admission_published=True,error=None,status='cancelled',media_cleanup_certified=False)
                return types.SimpleNamespace(run=run)
            def close(request_id):calls.append('close');return dict(actual_terminal_cleanup=False,receipt='Modeled physical barriers pending')
            bridge=self.make(folder,factory,close);start=self.request();self.assertEqual(bridge.dispatch(start,'t'*32)['state'],'queued');self.assertTrue(entered.wait(2))
            tick=time.monotonic();self.assertEqual(bridge.status(start['requestId'],'t'*32)['state'],'running')
            stop=self.request('disconnect');self.assertEqual(bridge.dispatch(stop,'t'*32)['state'],'queued');self.assertLess(time.monotonic()-tick,1)
            bridge.dispatch(stop,'t'*32);bridge.dispatch(start,'t'*32)
            with self.assertRaises(RuntimeError):bridge.dispatch(self.request(),'t'*32)
            release.set();self.assertTrue(bridge.join(3));self.assertEqual(calls,['start','close'])
            self.assertEqual(bridge.status(stop['requestId'],'t'*32)['state'],'unknown');self.assertFalse(bridge.status(stop['requestId'],'t'*32)['actualEffectsCompleted'])
    def test_auth_shape_busy_and_changed_actor(self):
        with tempfile.TemporaryDirectory() as folder:
            def factory(*,cancelled):raise AssertionError('No effects admitted')
            def close(_):raise AssertionError('No close admitted')
            bridge=self.make(folder,factory,close,available=lambda:False);request=self.request()
            with self.assertRaises(PermissionError):bridge.dispatch(request,'bad')
            with self.assertRaises(ValueError):bridge.dispatch(dict(request,direction='Q→W'),'t'*32)
            with self.assertRaises(RuntimeError):bridge.dispatch(request,'t'*32)
            bridge.lane_available=lambda:True;bridge.current_actors=lambda:dict(ACTORS,Q=dict(ACTORS['Q'],nativeGeneration='18'))
            bridge.dispatch(request,'t'*32);self.assertTrue(bridge.join(3));self.assertEqual(bridge.status(request['requestId'],'t'*32)['state'],'unknown')
    def test_uncertain_operation_and_conflicting_replay(self):
        count=[]
        with tempfile.TemporaryDirectory() as folder:
            def factory(*,cancelled):count.append('factory');raise RuntimeError('Unknown actual outcome')
            def close(_):return dict(actual_terminal_cleanup=True,receipt='Modeled actual terminal owner receipt')
            bridge=self.make(folder,factory,close);request=self.request();bridge.dispatch(request,'t'*32);self.assertTrue(bridge.join(3));self.assertEqual(bridge.dispatch(request,'t'*32)['state'],'unknown');self.assertEqual(count,['factory'])
            with self.assertRaises(ValueError):bridge.dispatch(dict(request,action='disconnect'),'t'*32)
            stop=self.request('disconnect');bridge.dispatch(stop,'t'*32);self.assertTrue(bridge.join(3));self.assertTrue(bridge.status(stop['requestId'],'t'*32)['actualEffectsCompleted'])
    def test_restart_durable_unknown_never_replays_effect(self):
        with tempfile.TemporaryDirectory() as folder:
            def factory(*,cancelled):raise AssertionError('Retained intent cannot execute')
            def close(_):raise AssertionError('No close')
            bridge=self.make(folder,factory,close);request=self.request();canonical=__import__('json').dumps(request,sort_keys=True,separators=(',',':')).encode();(pathlib.Path(folder)/(request['requestId']+'.intent.json')).write_bytes(canonical)
            self.assertEqual(bridge.dispatch(request,'t'*32)['state'],'unknown');self.assertIsNone(bridge.thread)
    def test_native_session_transition_keeps_ticket_and_stop_responsive(self):
        entered=threading.Event();release=threading.Event();current=['session.fixture'];calls=[]
        with tempfile.TemporaryDirectory() as folder:
            def factory(*,cancelled):
                def run():
                    current[0]='session.actual-model';bridge.anchor_validated_session(bridge.active_id,current[0]);bridge.publish_effective(bridge.active_id,'Modeled official bilateral arm/admission validation');entered.set()
                    with bridge.native_lock:release.wait(3)
                    self.assertTrue(cancelled());return types.SimpleNamespace(admission_published=True,error=None,status='cancelled')
                return types.SimpleNamespace(run=run)
            def close(_):calls.append('close');return dict(actual_terminal_cleanup=True,receipt='Modeled original close terminal')
            bridge=self.make(folder,factory,close);bridge.current_session=lambda:current[0];start=self.request();bridge.dispatch(start,'t'*32);self.assertTrue(entered.wait(2));readback=bridge.status(start['requestId'],'t'*32);self.assertEqual(readback['session'],'session.fixture');self.assertEqual(readback['actualNativeSession'],'session.actual-model');self.assertFalse(readback['actualEffectsCompleted'])
            stop=self.request('disconnect');tick=time.monotonic();bridge.dispatch(stop,'t'*32);self.assertLess(time.monotonic()-tick,1);release.set();self.assertTrue(bridge.join(3));self.assertEqual(calls,['close']);self.assertTrue(bridge.status(stop['requestId'],'t'*32)['actualEffectsCompleted']);self.assertTrue((pathlib.Path(folder)/(start['requestId']+'.session-transition.json')).exists())
            with self.assertRaises(ValueError):bridge.anchor_validated_session(start['requestId'],'session.changed')
if __name__=='__main__':unittest.main()
