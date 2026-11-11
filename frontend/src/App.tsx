import {useEffect,useState} from "react";
import {api,SchoolClass,Hold} from "./api";

const validStudent=(s:string)=>/^\d{17}$/.test(s);
function Countdown({expiresAt}:{expiresAt:string}){
  const [left,setLeft]=useState(Math.max(0,Math.floor((new Date(expiresAt).getTime()-Date.now())/1000)));
  useEffect(()=>{const t=setInterval(()=>setLeft(Math.max(0,Math.floor((new Date(expiresAt).getTime()-Date.now())/1000))),1000);return()=>clearInterval(t)},[expiresAt]);
  return <strong>{left>0?`${Math.floor(left/60)}:${String(left%60).padStart(2,"0")}`:"Expired"}</strong>;
}
export default function App(){
  const [mode,setMode]=useState<"parent"|"admin">("parent");
  const [student,setStudent]=useState("");
  const [classes,setClasses]=useState<SchoolClass[]>([]);
  const [holds,setHolds]=useState<Hold[]>([]);
  const [name,setName]=useState("");
  const [seats,setSeats]=useState("30");
  const [busy,setBusy]=useState(false);
  const [message,setMessage]=useState("");
  const [error,setError]=useState("");
  const load=async()=>{try{setClasses(await api.classes())}catch(e){setError((e as Error).message)}};
  const loadMine=async()=>{if(validStudent(student)){try{setHolds(await api.myHolds(student))}catch(e){setError((e as Error).message)}}};
  useEffect(()=>{load();const t=setInterval(load,5000);return()=>clearInterval(t)},[]);
  useEffect(()=>{loadMine()},[student]);
  const run=async(fn:()=>Promise<unknown>,success:string)=>{setBusy(true);setError("");setMessage("");try{await fn();setMessage(success);await load();await loadMine()}catch(e){setError((e as Error).message)}finally{setBusy(false)}};
  return <main className="shell">
    <header><div><span className="eyebrow">SCHOOL ADMISSION PORTAL</span><h1>Admission made simple.</h1><p>Fair, first-come-first-served seat allocation.</p></div><div className="switch"><button className={mode==="parent"?"active":""} onClick={()=>setMode("parent")}>Parent</button><button className={mode==="admin"?"active":""} onClick={()=>setMode("admin")}>School admin</button></div></header>
    {message&&<div className="notice success">{message}</div>}{error&&<div className="notice error">{error}</div>}
    {mode==="parent"?<>
      <section className="panel"><div className="section-head"><div><span className="eyebrow">STUDENT IDENTITY</span><h2>Find your admission options</h2></div></div>
        <label>17-digit birth registration number</label><input value={student} onChange={e=>setStudent(e.target.value.replace(/\D/g,"").slice(0,17))} placeholder="Enter student's birth registration number" inputMode="numeric"/>
        <small>{student.length}/17 digits. Your ID is used to identify your applications.</small>
      </section>
      <section><div className="section-head"><div><span className="eyebrow">AVAILABLE PROGRAMS</span><h2>Choose a class & shift</h2></div><button className="light" onClick={load}>Refresh</button></div>
        <div className="cards">{classes.map(c=><article className="class-card" key={c.id}><div className="card-top"><span className="tag">ADMISSION OPEN</span><span className="seat">{c.availableSeats} seats left</span></div><h3>{c.name}</h3><div className="stats"><div><b>{c.totalSeats}</b><small>Total</small></div><div><b>{c.heldSeats}</b><small>On hold</small></div><div><b>{c.confirmedSeats}</b><small>Confirmed</small></div></div><div className="meter"><span style={{width:`${Math.min(100,((c.heldSeats+c.confirmedSeats)/Math.max(1,c.totalSeats))*100)}%`}}/></div><button disabled={busy||!validStudent(student)||c.availableSeats<=0} onClick={()=>run(()=>api.createHold(c.id,student),"Seat reserved. Complete admission before the hold expires.")}>Apply & hold seat</button></article>)}</div>
      </section>
      <section className="panel"><div className="section-head"><div><span className="eyebrow">YOUR ACTIVITY</span><h2>My applications</h2></div><button className="light" disabled={!validStudent(student)} onClick={loadMine}>Refresh</button></div>
        {!validStudent(student)?<p className="muted">Enter a valid student ID to see applications.</p>:holds.length===0?<p className="muted">No applications yet.</p>:<div className="table-wrap"><table><thead><tr><th>Class</th><th>Status</th><th>Hold expires</th><th>Actions</th></tr></thead><tbody>{holds.map(h=><tr key={h.holdId}><td>{h.className}</td><td><span className={`status ${h.status.toLowerCase()}`}>{h.status}</span></td><td>{h.status==="HELD"?<Countdown expiresAt={h.expiresAt}/>:new Date(h.expiresAt).toLocaleString()}</td><td className="actions">{h.status==="HELD"&&<><button disabled={busy} onClick={()=>run(()=>api.confirm(h.holdId,student),"Admission confirmed (demo payment).")}>Pay admission fee</button><button className="danger" disabled={busy} onClick={()=>run(()=>api.cancel(h.holdId,student),"Application cancelled.")}>Cancel</button></>}</td></tr>)}</tbody></table></div>}
      </section>
    </>:<>
      <section className="panel"><span className="eyebrow">SCHOOL ADMINISTRATION</span><h2>Create a class / shift</h2><div className="form-grid"><div><label>Class and shift name</label><input value={name} onChange={e=>setName(e.target.value)} placeholder="e.g. Class 6 - Morning Shift"/></div><div><label>Total seats</label><input type="number" min="1" value={seats} onChange={e=>setSeats(e.target.value)}/></div><button disabled={busy||!name.trim()||Number(seats)<=0} onClick={()=>run(async()=>{await api.createClass(name.trim(),Number(seats));setName("");setSeats("30")},"Class created successfully.")}>Create class</button></div></section>
      <section><div className="section-head"><div><span className="eyebrow">LIVE OVERVIEW</span><h2>Admission capacity</h2></div><button className="light" onClick={load}>Refresh</button></div><div className="cards">{classes.map(c=><article className="class-card" key={c.id}><span className="tag">ACTIVE CLASS</span><h3>{c.name}</h3><div className="stats"><div><b>{c.totalSeats}</b><small>Total seats</small></div><div><b>{c.heldSeats}</b><small>Held</small></div><div><b>{c.confirmedSeats}</b><small>Confirmed</small></div></div><div className="availability"><b>{c.availableSeats}</b><span>Available seats</span></div></article>)}</div></section>
    </>}
    <footer>School Admission System · Demo payment only · Built with .NET, PostgreSQL and React</footer>
  </main>;
}
