import { FormEvent, useEffect, useState } from 'react';
import { CheckCircle2, ChevronDown, ChevronUp, GraduationCap, LogIn, LogOut, Plus, RefreshCw, Save, ShieldCheck, Users } from 'lucide-react';

type ClassInfo = { id: string; name: string; totalSeats: number; heldSeats: number; confirmedSeats: number; availableSeats: number; admissionFeeMinorUnits: number };
type AdminStudent = { holdId: string; studentId: string; studentName: string; confirmedAt: string | null };
type Admin = { userName: string };
type AdminPanelProps = { api: (path: string, options?: RequestInit) => Promise<any>; onClassesChanged: () => void };

export default function AdminPanel({ api, onClassesChanged }: AdminPanelProps) {
  const [admin, setAdmin] = useState<Admin | null>(null);
  const [checking, setChecking] = useState(true);
  const [registering, setRegistering] = useState(false);
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [registrationKey, setRegistrationKey] = useState('');
  const [classes, setClasses] = useState<ClassInfo[]>([]);
  const [capacity, setCapacity] = useState<Record<string, string>>({});
  const [rosters, setRosters] = useState<Record<string, AdminStudent[]>>({});
  const [openRoster, setOpenRoster] = useState<string | null>(null);
  const [className, setClassName] = useState('');
  const [seats, setSeats] = useState('30');
  const [fee, setFee] = useState('0');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  const loadClasses = async () => {
    const rows = await api('/classes') as ClassInfo[];
    setClasses(rows);
    setCapacity(current => Object.fromEntries(rows.map(row => [row.id, current[row.id] ?? String(row.totalSeats)])));
  };

  useEffect(() => {
    api('/admin/me').then((value: Admin) => setAdmin(value)).catch(() => setAdmin(null)).finally(() => setChecking(false));
  }, [api]);

  useEffect(() => {
    if (admin) void loadClasses().catch(reason => setError((reason as Error).message));
  }, [admin]);

  const submitAuth = async (event: FormEvent) => {
    event.preventDefault(); setBusy(true); setError(''); setNotice('');
    try {
      const endpoint = registering ? '/admin/register' : '/admin/login';
      const body = registering ? { userName, password, registrationKey } : { userName, password };
      const result = await api(endpoint, { method: 'POST', body: JSON.stringify(body) });
      setAdmin(result); setPassword(''); setRegistrationKey(''); setNotice(registering ? 'Administrator account created and signed in.' : 'Signed in to the school admin panel.');
    } catch (reason) { setError((reason as Error).message); }
    finally { setBusy(false); }
  };

  const createClass = async (event: FormEvent) => {
    event.preventDefault(); setBusy(true); setError(''); setNotice('');
    try {
      await api('/classes', { method: 'POST', body: JSON.stringify({ name: className, totalSeats: Number(seats), admissionFeeMinorUnits: Math.round(Number(fee) * 100) }) });
      setClassName(''); setNotice('Class created.'); await loadClasses(); onClassesChanged();
    } catch (reason) { setError((reason as Error).message); }
    finally { setBusy(false); }
  };

  const updateCapacity = async (classInfo: ClassInfo) => {
    setBusy(true); setError(''); setNotice('');
    try {
      await api(`/classes/${classInfo.id}/capacity`, { method: 'PUT', body: JSON.stringify({ totalSeats: Number(capacity[classInfo.id]) }) });
      setNotice(`${classInfo.name} seat capacity updated.`); await loadClasses(); onClassesChanged();
    } catch (reason) { setError((reason as Error).message); }
    finally { setBusy(false); }
  };

  const toggleRoster = async (classInfo: ClassInfo) => {
    if (openRoster === classInfo.id) { setOpenRoster(null); return; }
    setOpenRoster(classInfo.id); setError('');
    try {
      const roster = await api(`/admin/classes/${classInfo.id}/students`) as AdminStudent[];
      setRosters(current => ({ ...current, [classInfo.id]: roster }));
    }
    catch (reason) { setError((reason as Error).message); }
  };

  const logout = async () => {
    setBusy(true);
    try { await api('/admin/logout', { method: 'POST' }); setAdmin(null); setClasses([]); setNotice('Signed out.'); }
    catch (reason) { setError((reason as Error).message); }
    finally { setBusy(false); }
  };

  if (checking) return <section className="admin-panel"><div className="panel admin-loading">Checking administrator session...</div></section>;

  return <section className="admin-panel">
    <div className="section-heading admin-heading"><div><span className="eyebrow small">SCHOOL ADMINISTRATION</span><h2>{admin ? 'Admin dashboard' : 'School admin panel'}</h2><p>Manage classes, seat capacity, and confirmed admissions.</p></div>{admin && <div className="admin-signed-in"><ShieldCheck size={16}/><span>{admin.userName}</span><button className="btn btn-outline" disabled={busy} onClick={() => void logout()}><LogOut size={15}/> Sign out</button></div>}</div>
    {(error || notice) && <div role="status" className={`flash ${error ? 'flash-error' : 'flash-success'}`}><span>{error ? '!' : <CheckCircle2 size={17}/>}</span>{error || notice}<button onClick={() => { setError(''); setNotice(''); }} aria-label="Dismiss">x</button></div>}
    {!admin ? <div className="admin-auth panel">
      <div className="admin-auth-tabs"><button className={!registering ? 'active' : ''} onClick={() => { setRegistering(false); setError(''); }}>Sign in</button><button className={registering ? 'active' : ''} onClick={() => { setRegistering(true); setError(''); }}>Register administrator</button></div>
      <form className="admin-form" onSubmit={event => void submitAuth(event)}>
        <label>Username<input autoComplete="username" required minLength={3} maxLength={40} pattern="[A-Za-z0-9._-]{3,40}" value={userName} onChange={event => setUserName(event.target.value)} placeholder="schooladmin"/></label>
        <label>Password<input autoComplete={registering ? 'new-password' : 'current-password'} required type="password" minLength={12} maxLength={128} value={password} onChange={event => setPassword(event.target.value)} placeholder="At least 12 characters"/></label>
        {registering && <label>Administrator registration key<input required type="password" value={registrationKey} onChange={event => setRegistrationKey(event.target.value)} placeholder="Set in Docker environment"/></label>}
        <button className="btn btn-dark" disabled={busy}><LogIn size={16}/>{busy ? 'Please wait...' : registering ? 'Create admin account' : 'Sign in'}</button>
      </form>
      {registering && <p className="admin-security-note">The registration key is configured with <code>ADMIN_REGISTRATION_KEY</code> in Docker. Accounts use salted password hashing and expiring, HttpOnly sessions.</p>}
    </div> : <>
      <div className="admin-create panel"><div className="admin-block-heading"><span className="admin-block-icon"><Plus size={17}/></span><div><strong>Create a class and shift</strong><small>Each shift has its own seat capacity, for example Class 6 - Morning Shift.</small></div></div>
        <form className="create-form admin-create-form" onSubmit={event => void createClass(event)}><label>Class and shift name<input required maxLength={120} value={className} onChange={event => setClassName(event.target.value)} placeholder="Class 6 - Morning Shift"/></label><label>Seats<input required type="number" min="1" max="100000" value={seats} onChange={event => setSeats(event.target.value)}/></label><label>Admission fee (BDT)<input required type="number" min="0" step="0.01" value={fee} onChange={event => setFee(event.target.value)}/></label><button disabled={busy} className="btn btn-dark"><Plus size={15}/> Create class</button></form>
      </div>
      <div className="admin-classes-heading"><div><h3>Classes and admissions</h3><p>Update capacity and review the registration numbers of confirmed students.</p></div><button className="btn btn-outline" onClick={() => { void loadClasses(); }}><RefreshCw size={15}/> Refresh</button></div>
      {!classes.length ? <div className="empty slim panel">No classes yet. Create the first class above.</div> : <div className="admin-classes">{classes.map(classInfo => <article className="admin-class panel" key={classInfo.id}>
        <div className="admin-class-title"><span className="class-avatar avatar-0"><GraduationCap size={20}/></span><div><h4>{classInfo.name}</h4><small>{classInfo.confirmedSeats} confirmed / {classInfo.heldSeats} held / {classInfo.availableSeats} available</small></div></div>
        <form className="capacity-form" onSubmit={event => { event.preventDefault(); void updateCapacity(classInfo); }}><label>Seat capacity<input type="number" min={Math.max(1, classInfo.heldSeats + classInfo.confirmedSeats)} max="100000" required value={capacity[classInfo.id] ?? classInfo.totalSeats} onChange={event => setCapacity(current => ({ ...current, [classInfo.id]: event.target.value }))}/></label><span>Current: {classInfo.totalSeats}</span><button className="btn btn-outline" disabled={busy}><Save size={15}/> Save capacity</button></form>
        <button className="roster-toggle" onClick={() => void toggleRoster(classInfo)}>{openRoster === classInfo.id ? <ChevronUp size={16}/> : <Users size={16}/>} View admitted students ({classInfo.confirmedSeats}) {openRoster === classInfo.id ? <ChevronUp size={15}/> : <ChevronDown size={15}/>}</button>
        {openRoster === classInfo.id && <div className="roster-wrap">{(rosters[classInfo.id] ?? []).length ? <table className="roster-table"><thead><tr><th>Student name</th><th>Registration ID</th><th>Confirmed</th></tr></thead><tbody>{rosters[classInfo.id].map(student => <tr key={student.holdId}><td>{student.studentName}</td><td><code>{student.studentId}</code></td><td>{student.confirmedAt ? new Date(student.confirmedAt).toLocaleString() : '-'}</td></tr>)}</tbody></table> : <p className="roster-empty">No confirmed students yet.</p>}</div>}
      </article>)}</div>}
    </>}
  </section>;
}
