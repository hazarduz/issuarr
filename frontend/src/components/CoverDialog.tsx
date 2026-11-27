import { Check, ImagePlus, Link2, Loader2, RotateCcw, Upload } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { covers, mediaUrl, type CoverCandidate } from '../data/api';
import type { Title } from '../data/mock';
import { refreshLibrary } from '../data/store';
import { Modal } from './Modal';
import { useToast } from './Toast';

/** First page of a PDF rendered in the browser, so magazines can use their own cover. */
function PdfThumb({ url, onReady }: { url: string; onReady: (blob: Blob) => void }) {
  const ref = useRef<HTMLCanvasElement>(null);
  const [state, setState] = useState<'loading' | 'ok' | 'failed'>('loading');
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const pdfjs = await import('pdfjs-dist');
        const worker = await import('pdfjs-dist/build/pdf.worker.min.mjs?url');
        pdfjs.GlobalWorkerOptions.workerSrc = worker.default;
        const doc = await pdfjs.getDocument({ url, disableAutoFetch: true, rangeChunkSize: 1 << 18 }).promise;
        const page = await doc.getPage(1);
        const base = page.getViewport({ scale: 1 });
        const vp = page.getViewport({ scale: 600 / base.height });
        const canvas = ref.current;
        if (!canvas || cancelled) return;
        canvas.width = vp.width;
        canvas.height = vp.height;
        await page.render({ canvasContext: canvas.getContext('2d')!, viewport: vp, canvas }).promise;
        if (cancelled) return;
        setState('ok');
        canvas.toBlob((b) => b && onReady(b), 'image/jpeg', 0.9);
      } catch {
        if (!cancelled) setState('failed');
      }
    })();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line
  }, [url]);
  return (
    <>
      <canvas ref={ref} style={{ display: state === 'ok' ? 'block' : 'none' }} />
      {state === 'loading' && <Loader2 className="spin-icon" />}
      {state === 'failed' && <span className="muted">Couldn’t render</span>}
    </>
  );
}

type Choice = { kind: 'url'; url: string } | { kind: 'blob'; blob: Blob; preview: string };

export function CoverDialog({ title, onClose }: { title: Title; onClose: () => void }) {
  const toast = useToast();
  const [candidates, setCandidates] = useState<CoverCandidate[] | null>(null);
  const [failed, setFailed] = useState<Set<string>>(new Set());
  const [pdfBlobs, setPdfBlobs] = useState<Record<string, Blob>>({});
  const [choice, setChoice] = useState<Choice | null>(null);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [urlInput, setUrlInput] = useState('');
  const [busy, setBusy] = useState(false);
  const [dragging, setDragging] = useState(false);
  const [uploadPreview, setUploadPreview] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const hasCustom = !!title.coverUrl?.startsWith('/api/v1/mediacover/');

  useEffect(() => {
    covers.candidates(title.id).then(setCandidates).catch((e) => toast(e.message, 'error'));
    // eslint-disable-next-line
  }, [title.id]);

  const pickFile = (file: File | undefined) => {
    if (!file) return;
    if (!file.type.startsWith('image/')) return toast('Choose an image file (JPEG, PNG, WebP or GIF)', 'error');
    const preview = URL.createObjectURL(file);
    setUploadPreview(preview);
    setChoice({ kind: 'blob', blob: file, preview });
    setSelectedKey('upload');
  };

  const save = async () => {
    if (!choice) return;
    setBusy(true);
    try {
      if (choice.kind === 'blob') await covers.upload(title.id, choice.blob);
      else await covers.fromUrl(title.id, choice.url);
      await refreshLibrary();
      toast('Cover updated');
      onClose();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not set cover', 'error');
      setBusy(false);
    }
  };

  const reset = async () => {
    setBusy(true);
    try {
      await covers.reset(title.id);
      await refreshLibrary();
      toast('Cover reset');
      onClose();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not reset', 'error');
      setBusy(false);
    }
  };

  const shown = candidates?.filter((c) => !failed.has(c.url)) ?? [];

  return (
    <Modal
      title={`Cover · ${title.name}`}
      icon={<ImagePlus size={18} color="var(--accent)" />}
      wide
      onClose={onClose}
      footer={
        <>
          {hasCustom && (
            <button className="btn ghost" onClick={reset} disabled={busy} title="Go back to the metadata cover">
              <RotateCcw size={16} /> Reset to default
            </button>
          )}
          <span style={{ flex: 1 }} />
          <button className="btn ghost" onClick={onClose}>
            Cancel
          </button>
          <button className="btn primary" disabled={!choice || busy} onClick={save}>
            {busy ? <Loader2 size={16} className="spin-icon" /> : <Check size={16} />} Use this cover
          </button>
        </>
      }
    >
      <div
        className={`cover-grid ${dragging ? 'dragging' : ''}`}
        onDragOver={(e) => {
          e.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={(e) => {
          e.preventDefault();
          setDragging(false);
          pickFile(e.dataTransfer.files[0]);
        }}
      >
        <button type="button" className={`cover-option upload ${selectedKey === 'upload' ? 'on' : ''}`} onClick={() => fileInput.current?.click()}>
          {uploadPreview ? (
            <img src={uploadPreview} alt="" />
          ) : (
            <span>
              <Upload size={26} />
              <br />
              Upload or drop an image
            </span>
          )}
          <small>From your computer</small>
        </button>
        <input ref={fileInput} type="file" accept="image/*" hidden onChange={(e) => pickFile(e.target.files?.[0])} />

        {candidates === null && (
          <div className="cover-option">
            <Loader2 className="spin-icon" />
          </div>
        )}
        {shown.map((c) => {
          const key = c.source + c.url;
          const isPdf = c.source === 'pdf';
          const ready = !isPdf || pdfBlobs[c.url];
          return (
            <button
              key={key}
              type="button"
              className={`cover-option ${selectedKey === key ? 'on' : ''}`}
              disabled={!ready}
              onClick={() => {
                setSelectedKey(key);
                setChoice(isPdf ? { kind: 'blob', blob: pdfBlobs[c.url], preview: '' } : { kind: 'url', url: c.url });
              }}
            >
              {isPdf ? (
                <PdfThumb url={mediaUrl(c.url)!} onReady={(b) => setPdfBlobs((x) => ({ ...x, [c.url]: b }))} />
              ) : (
                <img src={mediaUrl(c.url)} alt="" loading="lazy" referrerPolicy="no-referrer" onError={() => setFailed((f) => new Set(f).add(c.url))} />
              )}
              <small>{c.label}</small>
            </button>
          );
        })}
      </div>
      {candidates && shown.length === 0 && <p className="muted">No covers found from metadata or files. Upload one or paste an image address below.</p>}

      <form
        style={{ display: 'flex', gap: 8, marginTop: 16 }}
        onSubmit={(e) => {
          e.preventDefault();
          if (!/^https?:\/\//i.test(urlInput.trim())) return toast('Paste a full http(s):// image address', 'error');
          setChoice({ kind: 'url', url: urlInput.trim() });
          setSelectedKey('pasted');
        }}
      >
        <div className="add-search" style={{ flex: 1, maxWidth: 'none' }}>
          <Link2 size={18} />
          <input style={{ height: 42, fontSize: 14 }} value={urlInput} onChange={(e) => setUrlInput(e.target.value)} placeholder="…or paste an image address (https://…)" />
        </div>
        <button className="btn" type="submit" style={{ height: 42 }}>
          Preview
        </button>
      </form>
      {selectedKey === 'pasted' && choice?.kind === 'url' && (
        <div className="cover-grid" style={{ marginTop: 12 }}>
          <div className="cover-option on">
            <img src={choice.url} alt="" referrerPolicy="no-referrer" onError={() => toast('That address didn’t load as an image', 'error')} />
            <small>Pasted</small>
          </div>
        </div>
      )}
    </Modal>
  );
}
